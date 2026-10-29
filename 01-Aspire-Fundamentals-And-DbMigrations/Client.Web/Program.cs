using Client.Web;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (OpenTelemetry, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Реєстрація Blazor компонентів
// =========================================================================
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

// =========================================================================
// 3. HTTP клієнт з підтримкою Aspire Service Discovery
//    "https+http://catalog-api" автоматично транслюється у реальний порт сервісу!
// =========================================================================
builder.Services.AddHttpClient<CatalogApiClient>(client =>
{
    client.BaseAddress = new("https+http://catalog-api");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.UseOutputCache();

app.MapRazorComponents<Client.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
