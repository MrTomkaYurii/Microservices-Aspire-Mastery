using Aspire.Hosting.Testing;
using Xunit;

namespace Tests.ClusterIntegration;

/// <summary>
/// 🧪 КРОК 1: ЖИТТЄВИЙ ЦИКЛ РОЗПОДІЛЕНОГО КЛАСТЕРА В ПАМ'ЯТІ (ASPIRE.HOSTING.TESTING)
/// 
/// 💡 В ЧОМУ РЕВОЛЮЦІЯ ASPIRE.HOSTING.TESTING?
/// Раніше для тестування мікросервісів доводилося вручну піднімати Docker Compose,
/// писати bash-скрипти очікування портів та молитись, щоб порти не були зайняті.
/// 
/// З 'Aspire.Hosting.Testing':
/// - xUnit запускає ВЕСЬ AppHost програмно в ізольованому тестовому оточенні.
/// - Всі порти виділяються динамічно (жодних конфліктів 'port already in use').
/// - Тести виконуються за лічені секунди та завершуються коректним Dispose всіх ресурсів.
/// </summary>
public class Step1_ClusterLifecycleTests
{
    [Fact]
    public async Task Cluster_StartsAndStopsCleanly_WithoutExceptions()
    {
        // 1. Створюємо білдер тестового додатку на основі реального AppHost
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aspire_AppHost>();

        // 2. Будуємо екземпляр кластера
        await using var app = await appHost.BuildAsync();

        // 3. Запускаємо кластер у пам'яті
        await app.StartAsync();

        // 4. Перевіряємо, що додаток успішно стартував
        Assert.NotNull(app);

        // 5. Зупиняємо додаток (Dispose виконається автоматично завдяки await using)
        await app.StopAsync();
    }
}
