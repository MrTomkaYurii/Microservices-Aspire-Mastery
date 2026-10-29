using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using Sender.Api.Models;

namespace Sender.Api.Controllers;

/// <summary>
/// =========================================================================================
/// 📘 КРОК 4: Відмовостійкість, TTL та Dead Letter Exchange (DLX)
/// =========================================================================================
/// 
/// 💡 ПРЕАМБУЛА ТА ЖИТТЄВА АНАЛОГІЯ:
/// Що робить пошта, якщо посилка має пошкоджену упаковку або адресат не приходить
/// за нею протягом 30 днів?
/// Чи повинен кур'єр нескінченно носити цю посилку туди-сюди кожного дня?
/// Звісно ні! Кур'єр швидко виб'ється із сил і не зможе доставляти інші важливі листи.
/// В реальному житті таку посилку переносять у спеціальний склад нерозібраних речей.
/// 
/// 🔍 ЧОМУ DLX НЕОБХІДНИЙ У РОЗПОДІЛЕНИХ СИСТЕМАХ:
/// У мікросервісах іноді трапляються "отруйні повідомлення" (Poison Messages) — наприклад,
/// пошкоджений JSON або замовлення на неіснуючого користувача.
/// Якщо обробник упаде з помилкою і зробить BasicNack(requeue: true), повідомлення миттєво
/// повернеться на початок черги. Обробник знову візьме його, знову впаде... і так мільйон разів!
/// Це називається **Retry Storm** (шторм повторів), який забиває процесор на 100%.
/// 
/// 🛡️ МЕХАНІЗМ DEAD LETTER EXCHANGE (DLX):
/// Черга налаштовується спеціальними параметрами брокера:
/// 1. `x-dead-letter-exchange`: ім'я біржі збоїв ('dlx-exchange').
/// 2. `x-dead-letter-routing-key`: ключ для маршрутизації в чергу помилок ('dead-letter-queue').
/// Повідомлення автоматично летить у чергу збоїв у двох випадках:
/// - Якщо вичерпано час життя TTL (Expiration);
/// - Якщо воркер викликав `BasicNackAsync(deliveryTag, multiple: false, requeue: false)`.
/// =========================================================================================
/// </summary>
[ApiController]
[Route("api/step4-dead-letter")]
[Produces("application/json")]
public class Step4_DeadLetterAndResilienceController : ControllerBase
{
    private readonly IConnection _connection;
    private readonly ILogger<Step4_DeadLetterAndResilienceController> _logger;

    public Step4_DeadLetterAndResilienceController(
        IConnection connection,
        ILogger<Step4_DeadLetterAndResilienceController> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    /// <summary>
    /// СЦЕНАРІЙ А: Відправити повідомлення з обмеженим часом життя (TTL).
    /// Якщо повідомлення не буде вичитане протягом заданого часу (за замовчуванням 5000ms),
    /// сам брокер RabbitMQ автоматично перенесе його в 'dead-letter-queue'.
    /// </summary>
    [HttpPost("send-with-ttl")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendWithTtl([FromBody] DeadLetterRequest request)
    {
        await using var channel = await _connection.CreateChannelAsync();

        // 1. Оголошуємо Dead Letter Exchange (спеціальну біржу для збійних повідомлень)
        await channel.ExchangeDeclareAsync("dlx-exchange", ExchangeType.Direct, durable: true);

        // 2. Оголошуємо робочу чергу та прив'язуємо до неї параметри перенаправлення
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

        // Встановлюємо заголовок Expiration (TTL у мілісекундах)
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

        _logger.LogWarning("⏳ [Step4 DLX TTL] Відправлено повідомлення з TTL {Ttl}ms у 'work-queue-with-dlx'.",
            request.TtlMilliseconds);

        return Ok(new
        {
            Mechanism = "Dead Letter Exchange через таймаут TTL",
            TargetQueue = "work-queue-with-dlx",
            TtlMilliseconds = request.TtlMilliseconds,
            DlxDestination = "dead-letter-queue",
            Explanation = $"Якщо ніхто не зчитає повідомлення протягом {request.TtlMilliseconds} мс, воно автоматично перелетить у 'dead-letter-queue'. Спостерігайте за цим у RabbitMQ Management UI!",
            request.Payload
        });
    }

    /// <summary>
    /// СЦЕНАРІЙ Б: Відправити "отруйне повідомлення" (Poison Message).
    /// Повідомлення містить маркер 'simulate-poison'. Воркер отримає його, виявить помилку
    /// і викличе BasicNackAsync(requeue: false), відправивши його у DLX без зависання системи!
    /// </summary>
    [HttpPost("send-poison-message")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendPoisonMessage()
    {
        await using var channel = await _connection.CreateChannelAsync();

        var poisonPayload = JsonSerializer.Serialize(new
        {
            OrderId = Guid.NewGuid(),
            Action = "simulate-poison",
            Description = "Це повідомлення навмисно містить помилку для демонстрації BasicNack(requeue: false)."
        });
        var body = Encoding.UTF8.GetBytes(poisonPayload);

        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };

        // Публікуємо в 'orders.critical'
        await channel.BasicPublishAsync(
            exchange: "direct-exchange",
            routingKey: "orders.critical",
            mandatory: true,
            basicProperties: props,
            body: body);

        _logger.LogError("☠️ [Step4 Poison] Надіслано тестове отруйне повідомлення в 'orders.critical'. Воркер відхилить його у DLX!");

        return Ok(new
        {
            Mechanism = "Poison Message Rejection -> DLX",
            RoutingKey = "orders.critical",
            Status = "Dispatched",
            Explanation = "Воркер зчитає цей запис, побачить маркер 'simulate-poison', виконає BasicNack(requeue: false), і повідомлення безпечно потрапить у DLX замість нескінченного зависання процесора."
        });
    }
}
