using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var builder = Host.CreateApplicationBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Підключення до RabbitMQ v7
// =========================================================================
builder.Services.AddSingleton<IConnection>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connString = config.GetConnectionString("messaging");

    var factory = new ConnectionFactory
    {
        Uri = new Uri(connString ?? "amqp://guest:guest@localhost:5672"),
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
    };

    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

// Реєструємо фонового воркера
builder.Services.AddHostedService<RabbitMqConsumerWorker>();

var host = builder.Build();
host.Run();

/// <summary>
/// Фоновий воркер, що демонструє повну низькорівневу топологію брокера:
/// черги, прив'язки (bindings), QoS prefetch та обробку Dead Letter Queue.
/// </summary>
public class RabbitMqConsumerWorker : BackgroundService
{
    private readonly IConnection _connection;
    private readonly ILogger<RabbitMqConsumerWorker> _logger;
    private IChannel? _channel;

    public RabbitMqConsumerWorker(IConnection connection, ILogger<RabbitMqConsumerWorker> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 [Receiver.Worker] Ініціалізація каналу та оголошення топології RabbitMQ v7...");

        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // =====================================================================
        // НАЛАШТУВАННЯ QoS (Quality of Service / Backpressure)
        // prefetchCount = 5 означає, що брокер не відправить воркеру більше 5
        // непідтверджених повідомлень одночасно. Це запобігає Out Of Memory!
        // =====================================================================
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 5, global: false, cancellationToken: stoppingToken);

        // ---------------------------------------------------------------------
        // 1. НАЛАШТУВАННЯ DIRECT EXCHANGE ТА ЧЕРГ
        // ---------------------------------------------------------------------
        await _channel.ExchangeDeclareAsync("direct-exchange", ExchangeType.Direct, durable: true, cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync("orders-critical-queue", durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync("orders-critical-queue", "direct-exchange", "orders.critical", cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync("orders-regular-queue", durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync("orders-regular-queue", "direct-exchange", "orders.regular", cancellationToken: stoppingToken);

        // ---------------------------------------------------------------------
        // 2. НАЛАШТУВАННЯ FANOUT EXCHANGE ТА ЧЕРГ
        // ---------------------------------------------------------------------
        await _channel.ExchangeDeclareAsync("fanout-exchange", ExchangeType.Fanout, durable: true, cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync("notifications-service-a-queue", durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync("notifications-service-a-queue", "fanout-exchange", string.Empty, cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync("notifications-service-b-queue", durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync("notifications-service-b-queue", "fanout-exchange", string.Empty, cancellationToken: stoppingToken);

        // ---------------------------------------------------------------------
        // 3. НАЛАШТУВАННЯ TOPIC EXCHANGE ТА ЧЕРГ
        // ---------------------------------------------------------------------
        await _channel.ExchangeDeclareAsync("topic-exchange", ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);

        // Маска "eu.#" ловить будь-які повідомлення з Європи (eu.orders.created, eu.payments.refund.success тощо)
        await _channel.QueueDeclareAsync("europe-all-queue", durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync("europe-all-queue", "topic-exchange", "eu.#", cancellationToken: stoppingToken);

        // Маска "*.orders.urgent" ловить термінові замовлення з БУДЬ-ЯКОГО регіону
        await _channel.QueueDeclareAsync("urgent-all-queue", durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync("urgent-all-queue", "topic-exchange", "*.orders.urgent", cancellationToken: stoppingToken);

        // ---------------------------------------------------------------------
        // 4. НАЛАШТУВАННЯ DEAD LETTER EXCHANGE (DLX) ТА ЧЕРГИ ЗБОЇВ
        // ---------------------------------------------------------------------
        await _channel.ExchangeDeclareAsync("dlx-exchange", ExchangeType.Direct, durable: true, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync("dead-letter-queue", durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync("dead-letter-queue", "dlx-exchange", "dead-letter-routing-key", cancellationToken: stoppingToken);

        _logger.LogInformation("✅ [Receiver.Worker] Усі exchanges, queues та bindings успішно сконфігуровані!");

        // =====================================================================
        // СТВОРЕННЯ КОНСЬЮМЕРІВ (AsyncEventingBasicConsumer у RabbitMQ v7)
        // =====================================================================

        // Консьюмер критичних замовлень
        await RegisterConsumer("orders-critical-queue", async (body, deliveryTag, channel) =>
        {
            _logger.LogInformation("🚨 [Critical Consumer] Отримано КРИТИЧНЕ замовлення: {Body}", body);

            // Демонстрація відхилення в DLX при імітації отруйного повідомлення (Poison Message)
            if (body.Contains("simulate-poison"))
            {
                _logger.LogError("☠️ [Poison Message] Знайдено маркер помилки! Робимо BasicNackAsync(requeue: false) -> летить в DLX!");
                await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false);
                return;
            }

            // Підтверджуємо успішну обробку (BasicAck)
            await channel.BasicAckAsync(deliveryTag, multiple: false);
        }, stoppingToken);

        // Консьюмер звичайних замовлень
        await RegisterConsumer("orders-regular-queue", async (body, deliveryTag, channel) =>
        {
            _logger.LogInformation("📦 [Regular Consumer] Отримано звичайне замовлення: {Body}", body);
            await channel.BasicAckAsync(deliveryTag, multiple: false);
        }, stoppingToken);

        // Консьюмер широкомовних повідомлень А
        await RegisterConsumer("notifications-service-a-queue", async (body, deliveryTag, channel) =>
        {
            _logger.LogInformation("🔔 [Notification Service A] Сповіщення отримано: {Body}", body);
            await channel.BasicAckAsync(deliveryTag, multiple: false);
        }, stoppingToken);

        // Консьюмер широкомовних повідомлень Б
        await RegisterConsumer("notifications-service-b-queue", async (body, deliveryTag, channel) =>
        {
            _logger.LogInformation("📬 [Notification Service B] Дублікат сповіщення отримано: {Body}", body);
            await channel.BasicAckAsync(deliveryTag, multiple: false);
        }, stoppingToken);

        // Консьюмер європейських подій Topic
        await RegisterConsumer("europe-all-queue", async (body, deliveryTag, channel) =>
        {
            _logger.LogInformation("🇪🇺 [Europe Topic Consumer] Подія з Європи: {Body}", body);
            await channel.BasicAckAsync(deliveryTag, multiple: false);
        }, stoppingToken);

        // Консьюмер термінових замовлень Topic
        await RegisterConsumer("urgent-all-queue", async (body, deliveryTag, channel) =>
        {
            _logger.LogInformation("⚡ [Urgent Topic Consumer] Термінова подія з будь-якого регіону: {Body}", body);
            await channel.BasicAckAsync(deliveryTag, multiple: false);
        }, stoppingToken);

        // Консьюмер Dead Letter Queue (кладовище повідомлень)
        await RegisterConsumer("dead-letter-queue", async (body, deliveryTag, channel) =>
        {
            _logger.LogCritical("💀 [DLX DEAD LETTER CONSUMER] Перехоплено повідомлення у Dead Letter Queue! (Протухло по TTL або відхилено з NACK): {Body}", body);
            await channel.BasicAckAsync(deliveryTag, multiple: false);
        }, stoppingToken);

        _logger.LogInformation("🎧 [Receiver.Worker] Усі підписники активні та очікують на повідомлення.");

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task RegisterConsumer(
        string queueName,
        Func<string, ulong, IChannel, Task> handler,
        CancellationToken ct)
    {
        if (_channel == null) return;

        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (sender, ea) =>
        {
            try
            {
                var bodyText = Encoding.UTF8.GetString(ea.Body.ToArray());
                await handler(bodyText, ea.DeliveryTag, _channel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Помилка обробки повідомлення з черги {Queue}", queueName);
                // Повертаємо повідомлення в чергу (requeue: true)
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        // autoAck: false обов'язковий для надійних систем (manual ACK/NACK)
        await _channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: ct);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Зупинка Receiver.Worker...");
        if (_channel != null)
        {
            await _channel.CloseAsync(cancellationToken);
            _channel.Dispose();
        }
        await base.StopAsync(cancellationToken);
    }
}
