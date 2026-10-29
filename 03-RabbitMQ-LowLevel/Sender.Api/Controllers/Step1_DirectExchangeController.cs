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
/// 3. Якщо черга з таким ключем відсутня, повідомлення просто буде знищене (або повернуте).
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
    /// Відправити повідомлення у Direct Exchange за точним ключем маршрутизації.
    /// Спробуйте передати RoutingKey = 'orders.critical' або 'orders.regular'.
    /// </summary>
    [HttpPost("send")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendDirect([FromBody] DirectMessageRequest request)
    {
        // КРОК 1: Створюємо легковаговий асинхронний канал (IChannel) під цей запит.
        // IChannel НЕ МОЖНА шарити між паралельними потоками!
        await using var channel = await _connection.CreateChannelAsync();

        // КРОК 2: Оголошуємо біржу типу Direct.
        // durable = true означає, що біржа не зникне при перезапуску брокера.
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

        // КРОК 3: Налаштовуємо властивості (Properties).
        // DeliveryMode = Persistent гарантує запис повідомлення на диск брокера.
        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = request.MessageId.ToString(),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        // КРОК 4: Публікуємо повідомлення. Брокер перевірить точний збіг RoutingKey.
        await channel.BasicPublishAsync(
            exchange: "direct-exchange",
            routingKey: request.RoutingKey,
            mandatory: true,
            basicProperties: props,
            body: body);

        _logger.LogInformation("📤 [Step1 Direct] Відправлено в 'direct-exchange' з RoutingKey '{Key}': {Payload}",
            request.RoutingKey, request.Payload);

        return Ok(new
        {
            Mechanism = "Direct Exchange (Точний збіг RoutingKey)",
            Exchange = "direct-exchange",
            request.RoutingKey,
            DestinationQueue = request.RoutingKey == "orders.critical" ? "orders-critical-queue" : "orders-regular-queue",
            Explanation = $"Брокер відправив це повідомлення тільки в чергу, яка має точний binding '{request.RoutingKey}'.",
            request.Payload
        });
    }
}
