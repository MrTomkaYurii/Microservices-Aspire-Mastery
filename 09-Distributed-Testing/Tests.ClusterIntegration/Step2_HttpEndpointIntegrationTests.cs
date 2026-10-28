using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using Xunit;

namespace Tests.ClusterIntegration;

/// <summary>
/// 🌐 КРОК 2: ІНТЕГРАЦІЙНІ HTTP-ТЕСТИ З АВТОМАТИЧНИМ SERVICE DISCOVERY
/// 
/// 💡 ЯК ПРАЦЮЄ app.CreateHttpClient("sample-api")?
/// Тестовий фреймворк Aspire автоматично налаштовує HttpClient:
/// - Знає випадковий динамічний порт, на якому піднявся контейнер/процес.
/// - Автоматично підставляє правильну базову URL-адресу.
/// - Включає трасування та політики стійкості.
/// </summary>
public class Step2_HttpEndpointIntegrationTests
{
    [Fact]
    public async Task GetAllItems_ReturnsSuccessAndInitialData()
    {
        // 1. Запуск тестового кластера
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aspire_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // 2. Створюємо типізований HTTP-клієнт до сервісу 'sample-api' за його іменем у Aspire
        var httpClient = app.CreateHttpClient("sample-api");

        // 3. Виконуємо реальний HTTP GET запит
        var response = await httpClient.GetAsync("/api/step1-items");

        // 4. Перевіряємо статус код та вміст
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<TestItemDto>>();
        Assert.NotNull(items);
        Assert.NotEmpty(items);
    }

    [Fact]
    public async Task CreateItem_ReturnsCreatedAndCanBeRetrieved()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aspire_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("sample-api");

        // 1. Створюємо новий елемент через POST
        var newRequest = new { Name = "Clean Architecture with Aspire", Price = 79.99m };
        var postResponse = await httpClient.PostAsJsonAsync("/api/step1-items", newRequest);

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var createdItem = await postResponse.Content.ReadFromJsonAsync<TestItemDto>();
        Assert.NotNull(createdItem);
        Assert.Equal("Clean Architecture with Aspire", createdItem.Name);

        // 2. Отримуємо його назад через GET за отриманим Id
        var getResponse = await httpClient.GetAsync($"/api/step1-items/{createdItem.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var retrievedItem = await getResponse.Content.ReadFromJsonAsync<TestItemDto>();
        Assert.NotNull(retrievedItem);
        Assert.Equal(createdItem.Id, retrievedItem.Id);
    }

    public record TestItemDto(Guid Id, string Name, decimal Price);
}
