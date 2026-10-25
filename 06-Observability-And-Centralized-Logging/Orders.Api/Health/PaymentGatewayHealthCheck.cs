using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orders.Api.Health;

/// <summary>
/// 🩺 Кастомний Health Check зовнішнього платіжного шлюзу (Payment Gateway).
/// Демонструє різницю між станами Healthy, Degraded та Unhealthy.
/// Має прапорець для інтерактивного тестування через Swagger.
/// </summary>
public class PaymentGatewayHealthCheck : IHealthCheck
{
    // Прапорець для симуляції збою зі Swagger
    public static HealthStatus SimulatedStatus = HealthStatus.Healthy;
    public static string StatusReason = "Платіжний шлюз Stripe працює в штатному режимі.";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return SimulatedStatus switch
        {
            HealthStatus.Healthy => Task.FromResult(HealthCheckResult.Healthy(StatusReason, new Dictionary<string, object>
            {
                ["LatencyMs"] = 28,
                ["Provider"] = "Stripe v2026",
                ["Region"] = "eu-central-1"
            })),

            HealthStatus.Degraded => Task.FromResult(HealthCheckResult.Degraded(StatusReason, data: new Dictionary<string, object>
            {
                ["LatencyMs"] = 1250,
                ["Warning"] = "Високий час відповіді платіжного провайдера"
            })),

            _ => Task.FromResult(HealthCheckResult.Unhealthy(StatusReason, data: new Dictionary<string, object>
            {
                ["ErrorCode"] = "GATEWAY_TIMEOUT",
                ["ActionRequired"] = "Переключити трафік на резервний шлюз"
            }))
        };
    }
}
