using System.Net;
using Aspire.Hosting.Testing;
using Xunit;

namespace Tests.ClusterIntegration;

/// <summary>
/// 🩺 КРОК 3: ТЕСТУВАННЯ ЕНДПОІНТІВ ЖИВУЧОСТІ КЛАСТЕРА (/ALIVE ТА /HEALTH)
/// 
/// 💡 НАВІЩО АВТОМАТИЗУВАТИ ТЕСТИ ЗДОРОВ'Я?
/// Перед деплоєм у Kubernetes важливо переконатися, що probes дадуть зелене світло
/// і сервіс не буде відхилено або перезапущено в безкінечному циклі CrashLoopBackOff.
/// </summary>
public class Step3_HealthCheckClusterTests
{
    [Fact]
    public async Task LivenessEndpoint_Returns200Healthy()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aspire_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("sample-api");

        // Перевіряємо Liveness Probe (/alive)
        var aliveResponse = await httpClient.GetAsync("/alive");
        Assert.Equal(HttpStatusCode.OK, aliveResponse.StatusCode);
    }

    [Fact]
    public async Task ReadinessEndpoint_Returns200Healthy()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aspire_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("sample-api");

        // Перевіряємо Readiness Probe (/health)
        var healthResponse = await httpClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
    }
}
