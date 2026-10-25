using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orders.Api.Health;

namespace Orders.Api.Controllers;

/// <summary>
/// 🩺 КРОК 1: ДВОРЕЖИМНІ ПЕРЕВІРКИ ЖИВУЧОСТІ (HEALTH CHECKS)
/// 
/// 💡 НАВІЩО ПОТРІБНІ ДВА РІЗНІ ЕНДПОІНТИ?
/// В мікросервісній інфраструктурі (Kubernetes, Aspire, AWS ECS) балансувальники навантаження 
/// повинні знати дві принципово різні речі:
/// 
/// 1. Liveness Probe (/alive): "Чи живий сам веб-сервер?"
///    - Перевіряє, чи не завис потік або процес.
///    - Якщо цей ендпоінт падає (500 або Timeout) -> оркестратор ВБИВАЄ контейнер і перезапускає його наново.
///    - Сюди НЕ МОЖНА підключати перевірку зовнішньої бази даних чи чужого API! 
///      (Інакше, якщо впаде база даних, впадуть і перезапустяться всі 50 реплік вашого сервісу, створивши лавину).
/// 
/// 2. Readiness Probe (/health): "Чи готовий сервіс обробляти замовлення клієнтів прямо зараз?"
///    - Перевіряє зв'язок з базою даних, Redis, платіжними шлюзами.
///    - Якщо статус Unhealthy -> контейнер НЕ вбивають, а просто тимчасово ПРИПИНЯЮТЬ посилати на нього клієнтські запити.
/// </summary>
[ApiController]
[Route("api/step1-health")]
public class Step1_HealthChecksController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;

    public Step1_HealthChecksController(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    /// <summary>
    /// Отримати поточний зведений стан здоров'я всіх зареєстрованих компонентів сервісу.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetHealthReport()
    {
        var report = await _healthCheckService.CheckHealthAsync();

        var details = report.Entries.Select(e => new
        {
            Component = e.Key,
            Status = e.Value.Status.ToString(),
            Description = e.Value.Description,
            DurationMs = e.Value.Duration.TotalMilliseconds,
            Tags = e.Value.Tags,
            Data = e.Value.Data
        });

        return Ok(new
        {
            Preamble = "Дворежимна система перевірки здоров'я в .NET Aspire.",
            OverallStatus = report.Status.ToString(),
            TotalDurationMs = report.TotalDuration.TotalMilliseconds,
            RegisteredProbes = details,
            StandardEndpoints = new
            {
                Liveness = "/alive (Тільки тег 'live' - чи живий сам процес Kestrel)",
                Readiness = "/health (Всі перевірки - чи доступні база даних та платіжний шлюз)"
            }
        });
    }

    /// <summary>
    /// Симулювати зміну стану платіжного шлюзу (Healthy, Degraded, Unhealthy) для перевірки реакції моніторингу.
    /// </summary>
    [HttpPost("simulate-status")]
    public IActionResult SimulatePaymentGatewayStatus([FromBody] SimulateHealthRequest request)
    {
        if (!Enum.TryParse<HealthStatus>(request.NewStatus, true, out var parsedStatus))
        {
            return BadRequest(new { Error = "Дозволені статуси: Healthy, Degraded, Unhealthy" });
        }

        PaymentGatewayHealthCheck.SimulatedStatus = parsedStatus;
        PaymentGatewayHealthCheck.StatusReason = request.Reason ?? $"Статус вручну змінено на {parsedStatus} зі Swagger";

        return Ok(new
        {
            Message = $"Стан PaymentGateway успішно змінено на '{parsedStatus}'.",
            Explanation = parsedStatus == HealthStatus.Healthy
                ? "Ендпоінт /health повертає 200 OK. Трафік іде в штатному режимі."
                : parsedStatus == HealthStatus.Degraded
                    ? "Ендпоінт /health повертає 200 OK (Degraded). Логується попередження, трафік не блокується."
                    : "Ендпоінт /health повертає 503 Service Unavailable. Оркестратор Aspire/K8s відведе трафік від цього інстансу!"
        });
    }
}

public record SimulateHealthRequest(string NewStatus, string? Reason);
