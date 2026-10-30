using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using Sender.Api.Models;

namespace Sender.Api.Controllers;

/// <summary>
/// =========================================================================================
/// 📘 КРОК 2: Широкомовна розсилка повідомлень (Fanout Exchange)
/// =========================================================================================
/// 
/// 💡 ПРЕАМБУЛА ТА ЖИТТЄВА АНАЛОГІЯ:
/// Уявіть шкільний дзвінок або радіогучномовець на вокзалі.
/// Коли диктор говорить у мікрофон, звук лунає по всіх коридорах і кімнатах одночасно.
/// Диктор не обирає конкретного учня чи пасажира — повідомлення чують абсолютно ВСІ.
/// 
/// 🔍 ЯК ЦЕ ПРАЦЮЄ В RABBITMQ:
/// 1. Біржа (Exchange) типу Fanout повністю ІГНОРУЄ ключ маршрутизації (RoutingKey).
/// 2. Скільки б черг не було підключено до біржі 'fanout-exchange', брокер зробить копію
///    повідомлення для КОЖНОЇ черги.
/// 3. У нашому прикладі копію отримають одночасно і 'notifications-service-a-queue',
///    і 'notifications-service-b-queue'.
/// 
/// 🔑 ДЛЯ ЧОГО ВИКОРИСТОВУЄТЬСЯ В АРХІТЕКТУРІ:
/// Публікація загальних подій (Broadcast Events):
/// - Оголошення технічного обслуговування системи.
/// - Реєстрація нового користувача (копію отримує сервіс надсилання email, сервіс аналітики і сервіс бонусів).
/// =========================================================================================
/// </summary>
[ApiController]
[Route("api/step2-fanout")]
[Produces("application/json")]
public class Step2_FanoutExchangeController : ControllerBase
{
    private readonly IConnection _connection;
    private readonly ILogger<Step2_FanoutExchangeController> _logger;

    public Step2_FanoutExchangeController(IConnection connection, ILogger<Step2_FanoutExchangeController> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    /// <summary>
    /// Відправити широкомовне сповіщення у Fanout Exchange (усі прив'язані черги отримають копію).
    /// </summary>
    [HttpPost("broadcast")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Broadcast([FromBody] FanoutMessageRequest request)
    {
        await using var channel = await _connection.CreateChannelAsync();

        // Оголошуємо біржу типу Fanout
        await channel.ExchangeDeclareAsync(
            exchange: "fanout-exchange",
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonDefaults.Options));

        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };

        // ВАЖЛИВО: У Fanout exchange поле routingKey залишається порожнім string.Empty,
        // тому що брокер не дивиться на ключі, а розмножує повідомлення по всіх підключених чергах!
        await channel.BasicPublishAsync(
            exchange: "fanout-exchange",
            routingKey: string.Empty,
            mandatory: false,
            basicProperties: props,
            body: body);

        _logger.LogInformation("📢 [Step2 Fanout] Широкомовне повідомлення розіслано в 'fanout-exchange': {Text}",
            request.BroadcastText);

        return Ok(new
        {
            Mechanism = "Fanout Exchange (Широкомовна розсилка всім чергам)",
            Exchange = "fanout-exchange",
            RoutingKeyUsed = "Ігнорується брокером (порожній рядок)",
            ReceivingQueues = new[] { "notifications-service-a-queue", "notifications-service-b-queue" },
            Explanation = "Брокер скопіював повідомлення в усі черги, підключені до біржі 'fanout-exchange'.",
            request.BroadcastText
        });
    }
}
