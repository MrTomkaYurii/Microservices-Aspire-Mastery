using MassTransit;
using Microsoft.AspNetCore.Connections;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Messaging.Events;
using System.Text;
using System.Text.Json;

public class BasketCheckoutEventHandlerWithoutMassTransit : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;   // для створення скоупів (щоб передати скоуп обджект)
    private readonly ILogger<BasketCheckoutEventHandlerWithoutMassTransit> _logger;
    private IChannel? _channel;                             // Новий асинхронний канал v7+

    public BasketCheckoutEventHandlerWithoutMassTransit(
        IConnection connection,
        IServiceScopeFactory scopeFactory,
        ILogger<BasketCheckoutEventHandlerWithoutMassTransit> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await _connection.CreateChannelAsync(); // новий API

        // Створення exchange
        await _channel.ExchangeDeclareAsync(
            exchange: "basket-checkout-exchange",
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // Створення черги
        var queueDeclare = await _channel.QueueDeclareAsync(
            queue: "basket-checkout-queue",
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // Прив’язуємо чергу до exchange
        await _channel.QueueBindAsync(
            queue: queueDeclare.QueueName,
            exchange: "basket-checkout-exchange",
            routingKey: string.Empty,
            cancellationToken: stoppingToken);

        // Обмежуємо кількість непідтверджених повідомлень
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            cancellationToken: stoppingToken);

        // Створюємо consumer
        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (sender, ea) =>
        {
            try
            {
                var message = Encoding.UTF8.GetString(ea.Body.ToArray());
                var basketCheckoutEvent = JsonSerializer.Deserialize<BasketCheckoutIntegrationEvent>(message);

                if (basketCheckoutEvent is null)
                {
                    _logger.LogWarning("Received null message from queue {Queue}", queueDeclare.QueueName);
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                    return;
                }

                _logger.LogInformation("Basket checkout event received: {UserName}, {TotalPrice}",
                    basketCheckoutEvent.UserName, basketCheckoutEvent.TotalPrice);

                using var scope = _scopeFactory.CreateScope();
                var orderService = scope.ServiceProvider.GetRequiredService<OrderService>();

                var order = new Order
                {
                    UserName = basketCheckoutEvent.UserName,
                    TotalPrice = basketCheckoutEvent.TotalPrice,
                    FirstName = basketCheckoutEvent.FirstName,
                    LastName = basketCheckoutEvent.LastName,
                    EmailAddress = basketCheckoutEvent.EmailAddress,
                    AddressLine = basketCheckoutEvent.AddressLine
                };

                await orderService.CreateOrderAsync(order);

                // підтверджуємо
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing BasketCheckoutIntegrationEvent");
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        // Починаємо споживання
        await _channel.BasicConsumeAsync(
            queue: queueDeclare.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("BasketCheckoutEventHandler started listening on {Queue}", queueDeclare.QueueName);

        // Чекаємо нескінченно, поки сервіс не зупинять
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping BasketCheckoutEventHandler...");

        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
            await _channel.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
