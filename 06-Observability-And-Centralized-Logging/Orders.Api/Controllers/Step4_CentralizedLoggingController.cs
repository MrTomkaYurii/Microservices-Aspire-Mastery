using Microsoft.AspNetCore.Mvc;

namespace Orders.Api.Controllers;

/// <summary>
/// 📝 КРОК 4: СТРУКТУРОВАНЕ СЕМАНТИЧНЕ ЛОГУВАННЯ (SERILOG + SEQ CENTRALIZED LOGS)
/// 
/// 💡 ЧОМУ ЗВИЧАЙНИЙ ТЕКСТОВИЙ ЛОГ НЕ ПРАЦЮЄ У ПРОДАКШЕНІ?
/// Погано (інтерполяція рядків):
///   logger.LogInformation($"Order {orderId} failed for user {userId} with error {error}");
///   В базі логів це просто один довгий неструктурований рядок. Знайти всі замовлення користувача #42 неможливо
///   без важкого Full-Text Scan по терабайтах даних.
/// 
/// Правильно (структурований шаблон Serilog):
///   logger.LogInformation("Order {OrderId} failed for user {UserId} with error {ErrorCode}", orderId, userId, error);
///   Seq/Elasticsearch парсить це як окремі індексовані колонки:
///   @Properties.OrderId == "ORD-100" AND @Properties.UserId == "USR-5"
/// </summary>
[ApiController]
[Route("api/step4-logging")]
public class Step4_CentralizedLoggingController : ControllerBase
{
    private readonly ILogger<Step4_CentralizedLoggingController> _logger;

    public Step4_CentralizedLoggingController(ILogger<Step4_CentralizedLoggingController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Відправити структуровані логи з контекстним Scope безпосередньо в централізований Seq.
    /// </summary>
    [HttpPost("log-event")]
    public IActionResult LogStructuredEvent([FromBody] StructuredLogRequest request)
    {
        // Створюємо логічний Scope (контекст), який додасться до абсолютно ВСІХ логів усередині блоку
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = Guid.NewGuid().ToString("N"),
            ["TenantId"] = request.TenantId,
            ["Environment"] = "Development-Aspire"
        }))
        {
            _logger.LogInformation("📦 Отримано замовлення {OrderId} на суму {Amount} {Currency} від клієнта {CustomerId}",
                request.OrderId, request.Amount, request.Currency, request.CustomerId);

            if (request.Amount > 1000)
            {
                _logger.LogWarning("⚠️ Замовлення {OrderId} перевищує поріг ліміту {Threshold}. Потрібна верифікація менеджера.",
                    request.OrderId, 1000);
            }

            _logger.LogInformation("✅ Замовлення {OrderId} успішно передано на склад.", request.OrderId);
        }

        return Ok(new
        {
            Preamble = "Структуровані події надіслано в Seq через Serilog.Sinks.Seq.",
            request.OrderId,
            SeqQueryExample = $"У веб-інтерфейсі Seq ви можете виконати запит: OrderId = '{request.OrderId}' або TenantId = '{request.TenantId}'",
            Benefits = "Миттєвий пошук без навантаження на диск, фільтрація за будь-яким JSON-полем, агрегація за лічені мілісекунди."
        });
    }

    /// <summary>
    /// Симулювати структурований запис помилки із винятком (Exception) для аналізу в Seq.
    /// </summary>
    [HttpPost("simulate-error")]
    public IActionResult SimulateExceptionLog([FromQuery] string orderId)
    {
        try
        {
            throw new InvalidOperationException($"Платіжний ліміт для замовлення {orderId} вичерпано на стороні еквайра.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Збій виконання фінансової транзакції для замовлення {OrderId}", orderId);
        }

        return Ok(new
        {
            Message = $"Виняток для замовлення '{orderId}' успішно зафіксовано в логах.",
            SeqTip = "У Seq відкрийте картку події: ви побачите повний StackTrace, тип Exception та всі параметри запиту."
        });
    }
}

public record StructuredLogRequest(string OrderId, string CustomerId, string TenantId, decimal Amount, string Currency = "USD");
