using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Sender.Api.Models;

/// <summary>
/// Стандартні налаштування JSON-серіалізації:
/// UnsafeRelaxedJsonEscaping дозволяє виводити чисту кирилицю та емодзі без \uXXXX екранування.
/// </summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };
}

/// <summary>
/// Запит на відправку прямого повідомлення через Direct Exchange.
/// </summary>
public class DirectMessageRequest
{
    /// <summary>
    /// Унікальний ідентифікатор повідомлення.
    /// </summary>
    [DefaultValue("3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    public Guid MessageId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Точний ключ маршрутизації: 'orders.critical' або 'orders.regular'.
    /// За замовчуванням встановлено 'orders.critical'.
    /// </summary>
    [DefaultValue("orders.critical")]
    public string RoutingKey { get; set; } = "orders.critical";

    /// <summary>
    /// Вміст замовлення.
    /// </summary>
    [DefaultValue("Критичне замовлення №100")]
    public string Payload { get; set; } = "Критичне замовлення №100";
}

/// <summary>
/// Спрощений запит на відправку в 1 клік (без необхідності вказувати routingKey).
/// </summary>
public class SimpleOrderRequest
{
    [DefaultValue("Замовлення через зручний ендпоінт")]
    public string Payload { get; set; } = "Замовлення через зручний ендпоінт";
}

public record FanoutMessageRequest(string BroadcastText);
public record TopicMessageRequest(string TopicRoutingKey, string Data);
public record DeadLetterRequest(string Payload, int TtlMilliseconds = 5000);
