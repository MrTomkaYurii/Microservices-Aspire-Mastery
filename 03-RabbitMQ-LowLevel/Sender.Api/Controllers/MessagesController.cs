using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using Sender.Api.Models;

namespace Sender.Api.Controllers;

/// <summary>
/// Контролер відправки повідомлень у брокер RabbitMQ через сучасний асинхронний SDK v7.
/// Демонструє роботу з Direct, Fanout, Topic біржами та механізм Dead Letter Exchange (DLX).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class MessagesController : ControllerBase
{
    private readonly IConnection _connection;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(IConnection connection, ILogger<MessagesController> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    /// <summary>
    /// Відправити повідомлення у Direct Exchange (маршрутизація за точним співпадінням Routing Key).
    /// </summary>
    /// <param name="request">Дані запиту: MessageId, RoutingKey ('orders.critical' або 'orders.regular'), Payload.</param>
    [HttpPost("direct")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendDirectMessage([FromBody] DirectMessageRequest request)
    {
        // У RabbitMQ v7 канали створюються асинхронно через CreateChannelAsync
        await using var channel = await _connection.CreateChannelAsync();

        // Оголошуємо Direct Exchange (durable = true)
        await channel.ExchangeDeclareAsync(
            exchange: "direct-exchange",
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);

        var payload = JsonSerializer.Serialize(new
        {
            request.MessageId,
            request.Payload,
            DispatchedAtUtc = DateTime.UtcNow
        });
        var body = Encoding.UTF8.GetBytes(payload);

        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent, // Зберігати на диск у разі збою брокера
            MessageId = request.MessageId.ToString(),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        // Публікуємо повідомлення з конкретним RoutingKey
        await channel.BasicPublishAsync(
            exchange: "direct-exchange",
            routingKey: request.RoutingKey,
            mandatory: true,
            basicProperties: props,
            body: body);

        _logger.LogInformation("📤 [Direct] Відправлено в 'direct-exchange' з RoutingKey '{Key}': {Payload}",
            request.RoutingKey, request.Payload);

        return Ok(new
        {
            Status = "Dispatched",
            Exchange = "direct-exchange",
            request.RoutingKey,
            request.Payload
        });
    }

    /// <summary>
    /// Відправити широкомовне сповіщення у Fanout Exchange (усі підписані черги отримають копію).
    /// </summary>
    /// <param name="request">Текст широкомовного сповіщення.</param>
    [HttpPost("fanout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendFanoutMessage([FromBody] FanoutMessageRequest request)
    {
        await using var channel = await _connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(
            exchange: "fanout-exchange",
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };

        // У Fanout exchange routingKey ігнорується брокером повністю
        await channel.BasicPublishAsync(
            exchange: "fanout-exchange",
            routingKey: string.Empty,
            mandatory: false,
            basicProperties: props,
            body: body);

        _logger.LogInformation("📢 [Fanout] Широкомовне сповіщення відправлено в 'fanout-exchange': {Text}", request.BroadcastText);

        return Ok(new
        {
            Status = "Broadcasted",
            Exchange = "fanout-exchange",
            request.BroadcastText
        });
    }

    /// <summary>
    /// Відправити повідомлення у Topic Exchange (маршрутизація за масками та шаблонами слів '*' та '#').
    /// </summary>
    /// <param name="request">Ключ маршрутизації (напр: 'eu.orders.urgent') та корисне навантаження.</param>
    [HttpPost("topic")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendTopicMessage([FromBody] TopicMessageRequest request)
    {
        await using var channel = await _connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(
            exchange: "topic-exchange",
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };

        await channel.BasicPublishAsync(
            exchange: "topic-exchange",
            routingKey: request.TopicRoutingKey,
            mandatory: true,
            basicProperties: props,
            body: body);

        _logger.LogInformation("🏷️ [Topic] Відправлено в 'topic-exchange' з ключем '{TopicKey}': {Data}",
            request.TopicRoutingKey, request.Data);

        return Ok(new
        {
            Status = "Dispatched",
            Exchange = "topic-exchange",
            request.TopicRoutingKey,
            request.Data
        });
    }

    /// <summary>
    /// Відправити повідомлення з обмеженим TTL (Time-To-Live) для демонстрації Dead Letter Exchange (DLX).
    /// Якщо повідомлення не буде оброблено за вказаний час, воно автоматично потрапить у 'dead-letter-queue'.
    /// </summary>
    /// <param name="request">Повідомлення та час життя у мілісекундах (за замовчуванням 5000ms).</param>
    [HttpPost("dead-letter-test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendDeadLetterTest([FromBody] DeadLetterRequest request)
    {
        await using var channel = await _connection.CreateChannelAsync();

        // 1. Оголошуємо Dead-Letter Exchange
        await channel.ExchangeDeclareAsync("dlx-exchange", ExchangeType.Direct, durable: true);

        // 2. Оголошуємо чергу з аргументами перенаправлення
        var queueArgs = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = "dlx-exchange",
            ["x-dead-letter-routing-key"] = "dead-letter-routing-key"
        };

        await channel.QueueDeclareAsync(
            queue: "work-queue-with-dlx",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArgs);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

        // Встановлюємо TTL через заголовок Expiration
        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Expiration = request.TtlMilliseconds.ToString()
        };

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: "work-queue-with-dlx",
            mandatory: true,
            basicProperties: props,
            body: body);

        _logger.LogWarning("⏳ [DLX Test] Відправлено повідомлення з TTL {Ttl}ms у 'work-queue-with-dlx'.", request.TtlMilliseconds);

        return Ok(new
        {
            Message = $"Повідомлення відправлено з TTL {request.TtlMilliseconds}ms. Спостерігайте за ним у RabbitMQ Management UI!",
            request.Payload
        });
    }
}
