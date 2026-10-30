using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using Sender.Api.Models;

namespace Sender.Api.Controllers;

/// <summary>
/// =========================================================================================
/// 📘 КРОК 1: Пряма маршрутизація повідомлень (Direct Exchange)
/// =========================================================================================
/// 
/// 💡 ПРЕАМБУЛА ТА ЖИТТЄВА АНАЛОГІЯ:
/// Уявіть поштове відділення з абонентськими скриньками.
/// На кожній скриньці наклеєно точний номер: "Квартира 10" або "Квартира 25".
/// Коли поштар бере конверт, на якому написано "Квартира 10", він опускає його
/// ТІЛЬКИ в скриньку "Квартира 10". Інші скриньки цей лист не отримають.
/// 
/// 🔍 ЯК ЦЕ ПРАЦЮЄ В RABBITMQ:
/// 1. Біржа (Exchange) типу Direct порівнює ключ маршрутизації (RoutingKey) повідомлення
///    із ключем прив'язки (BindingKey) черг.
/// 2. Якщо RoutingKey = "orders.critical", брокер покладе повідомлення
///    ВИКЛЮЧНО в чергу 'orders-critical-queue'.
/// 3. Якщо черга з таким ключем відсутня, повідомлення просто буде знищене брокером.
/// 
/// 🔑 ДЛЯ ЧОГО ВИКОРИСТОВУЄТЬСЯ В АРХІТЕКТУРІ:
/// Розподіл завдань за пріоритетами або категоріями:
/// - Критичні замовлення ("orders.critical") обробляються окремим пулом швидких воркерів.
/// - Звичайні замовлення ("orders.regular") обробляються повільніше у фоновому режимі.
/// =========================================================================================
/// </summary>
[ApiController]
[Route("api/step1-direct")]
[Produces("application/json")]
public class Step1_DirectExchangeController : ControllerBase
{
    private readonly IConnection _connection;
    private readonly ILogger<Step1_DirectExchangeController> _logger;

    public Step1_DirectExchangeController(IConnection connection, ILogger<Step1_DirectExchangeController> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    /// <summary>
    /// Відправити повідомлення у Direct Exchange за вказаним ключем.
    /// Дозволені ключі: 'orders.critical' (черга orders-critical-queue) та 'orders.regular' (orders-regular-queue).
    /// Якщо передано дефолтне 'string' або порожній рядок, автоматично нормалізується до 'orders.critical'.
    /// </summary>
    [HttpPost("send")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendDirect([FromBody] DirectMessageRequest request)
    {
        // Автоматична нормалізація дефолтних заглушок Swagger або порожніх рядків
        var routingKey = request.RoutingKey?.Trim();
        if (string.IsNullOrWhiteSpace(routingKey) || routingKey.Equals("string", StringComparison.OrdinalIgnoreCase))
        {
            routingKey = "orders.critical";
            _logger.LogWarning("⚠️ [Step1 Direct] Виявлено дефолтну заглушку або порожній ключ. Автоматично замінено на '{Key}'", routingKey);
        }

        // Перевірка допустимості ключа для запобігання втраті повідомлень
        var validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "orders.critical", "orders.regular", "orders-critical-queue", "orders-regular-queue"
        };

        if (!validKeys.Contains(routingKey))
        {
            return BadRequest(new
            {
                Error = $"Недійсний RoutingKey '{routingKey}'!",
                AllowedKeys = new[] { "orders.critical", "orders.regular" },
                Message = "У Direct Exchange повідомлення доставляється ТІЛЬКИ в чергу з точним збігом ключа. Будь-який сторонній ключ призведе до відхилення повідомлення."
            });
        }

        return await PublishToDirectExchangeAsync(routingKey, request.MessageId, request.Payload);
    }

    /// <summary>
    /// Зручний ендпоінт в 1 клік: відправити КРИТИЧНЕ замовлення ('orders.critical' -> 'orders-critical-queue').
    /// </summary>
    [HttpPost("send-critical")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendCritical([FromBody] SimpleOrderRequest request)
    {
        return await PublishToDirectExchangeAsync("orders.critical", Guid.NewGuid(), request.Payload);
    }

    /// <summary>
    /// Зручний ендпоінт в 1 клік: відправити ЗВИЧАЙНЕ замовлення ('orders.regular' -> 'orders-regular-queue').
    /// </summary>
    [HttpPost("send-regular")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendRegular([FromBody] SimpleOrderRequest request)
    {
        return await PublishToDirectExchangeAsync("orders.regular", Guid.NewGuid(), request.Payload);
    }

    private async Task<IActionResult> PublishToDirectExchangeAsync(string routingKey, Guid messageId, string payloadText)
    {
        await using var channel = await _connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(
            exchange: "direct-exchange",
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);

        var payload = JsonSerializer.Serialize(new
        {
            MessageId = messageId,
            Payload = payloadText,
            DispatchedAtUtc = DateTime.UtcNow
        }, JsonDefaults.Options);
        var body = Encoding.UTF8.GetBytes(payload);

        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = messageId.ToString(),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        await channel.BasicPublishAsync(
            exchange: "direct-exchange",
            routingKey: routingKey,
            mandatory: true,
            basicProperties: props,
            body: body);

        var destinationQueue = (routingKey == "orders.critical" || routingKey == "orders-critical-queue")
            ? "orders-critical-queue"
            : "orders-regular-queue";

        _logger.LogInformation("📤 [Step1 Direct] Відправлено в 'direct-exchange' з RoutingKey '{Key}' -> '{Queue}': {Payload}",
            routingKey, destinationQueue, payloadText);

        return Ok(new
        {
            Mechanism = "Direct Exchange (Точний збіг RoutingKey)",
            Exchange = "direct-exchange",
            RoutingKey = routingKey,
            DestinationQueue = destinationQueue,
            Explanation = $"Брокер успішно доставив це повідомлення в чергу '{destinationQueue}'.",
            Payload = payloadText
        });
    }
}
