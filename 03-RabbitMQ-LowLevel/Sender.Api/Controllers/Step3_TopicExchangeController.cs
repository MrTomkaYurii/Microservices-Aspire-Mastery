using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using Sender.Api.Models;

namespace Sender.Api.Controllers;

/// <summary>
/// =========================================================================================
/// 📘 КРОК 3: Маршрутизація за шаблонами та масками (Topic Exchange)
/// =========================================================================================
/// 
/// 💡 ПРЕАМБУЛА ТА ЖИТТЄВА АНАЛОГІЯ:
/// Уявіть сортувальний центр пошти міжнародної доставки.
/// Кожна посилка має маркування зі слів через крапку: "Регіон.Категорія.Пріоритет".
/// Наприклад: "eu.orders.urgent" (Європа, замовлення, терміново) або "us.payments.regular".
/// 
/// Сортувальники мають фільтри-маски:
/// - Сортувальник 1 має фільтр "eu.#" — він забирає абсолютно ВСІ посилки з Європи,
///   незалежно від категорії та пріоритету (# замінює будь-яку кількість слів).
/// - Сортувальник 2 має фільтр "*.orders.urgent" — він забирає термінові замовлення
///   з БУДЬ-ЯКОЇ країни світу (* замінює рівно одне слово регіону).
/// 
/// 🔍 СПЕЦСИМВОЛИ ШАБЛОНІВ У RABBITMQ:
/// 1. `*` (зірочка) — замінює РІВНО ОДНЕ слово між крапками.
/// 2. `#` (решітка) — замінює НУЛЬ АБО БІЛЬШЕ слів будь-якої довжини.
/// 
/// 🔑 ДЛЯ ЧОГО ВИКОРИСТОВУЄТЬСЯ В АРХІТЕКТУРІ:
/// Гнучка підписка на події складної розподіленої системи:
/// Сервіс може підписатися лише на ті події, які його дійсно цікавлять
/// (наприклад, тільки помилки платежів: "payments.*.failed").
/// =========================================================================================
/// </summary>
[ApiController]
[Route("api/step3-topic")]
[Produces("application/json")]
public class Step3_TopicExchangeController : ControllerBase
{
    private readonly IConnection _connection;
    private readonly ILogger<Step3_TopicExchangeController> _logger;

    public Step3_TopicExchangeController(IConnection connection, ILogger<Step3_TopicExchangeController> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    /// <summary>
    /// Відправити повідомлення у Topic Exchange за ключем формату 'регіон.сутність.пріоритет'.
    /// Спробуйте відправити:
    /// - 'eu.orders.urgent' -> потрапить і в 'europe-all-queue', і в 'urgent-all-queue'!
    /// - 'us.orders.urgent' -> потрапить тільки в 'urgent-all-queue'.
    /// - 'eu.orders.regular' -> потрапить тільки в 'europe-all-queue'.
    /// </summary>
    [HttpPost("send")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendTopic([FromBody] TopicMessageRequest request)
    {
        await using var channel = await _connection.CreateChannelAsync();

        // Оголошуємо біржу типу Topic
        await channel.ExchangeDeclareAsync(
            exchange: "topic-exchange",
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonDefaults.Options));

        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };

        // Публікуємо з ключем маршрутизації (наприклад: "eu.orders.urgent")
        await channel.BasicPublishAsync(
            exchange: "topic-exchange",
            routingKey: request.TopicRoutingKey,
            mandatory: true,
            basicProperties: props,
            body: body);

        _logger.LogInformation("🏷️ [Step3 Topic] Відправлено в 'topic-exchange' з ключем '{TopicKey}': {Data}",
            request.TopicRoutingKey, request.Data);

        return Ok(new
        {
            Mechanism = "Topic Exchange (Маршрутизація за масками * та #)",
            Exchange = "topic-exchange",
            request.TopicRoutingKey,
            MatchingRules = new[]
            {
                "Черга 'europe-all-queue' має підписку 'eu.#' -> ловить будь-які повідомлення з Європи.",
                "Черга 'urgent-all-queue' має підписку '*.orders.urgent' -> ловить термінові замовлення з будь-якого континенту."
            },
            request.Data
        });
    }
}
