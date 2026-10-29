using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Observability.Aspire.Demo.Health;

public class PaymentGatewayHealthCheck : IHealthCheck
{
    private static readonly Random Random = new();

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Simulate health check probe to downstream payment provider
        var latencyMs = Random.Next(10, 150);

        if (latencyMs > 130)
        {
            return Task.FromResult(HealthCheckResult.Degraded($"Payment Gateway latency is unusually high ({latencyMs}ms)."));
        }

        return Task.FromResult(HealthCheckResult.Healthy($"Payment Gateway is operational ({latencyMs}ms latency)."));
    }
}
