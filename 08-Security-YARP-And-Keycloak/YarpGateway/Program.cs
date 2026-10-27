using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================================
// 🎯 МОДУЛЬ 08: YARPGATEWAY - ЗОВНІШНІЙ ШЛЮЗ, БЕЗПЕКА ТА YARP REVERSE PROXY
// =========================================================================================

builder.AddServiceDefaults();

// 1. Налаштування обмеження частоти запитів (Rate Limiting) для захисту від DDoS
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.PermitLimit = 50;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 10;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// 2. Реєстрація YARP Reverse Proxy з конфігурації appsettings.json
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// 3. Контролери та Swagger для шлюзу
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "YARP API Gateway (.NET 10)",
        Version = "v1",
        Description = "Публічний вхідний шлюз системи: зворотний проксі-сервер, Token Propagation, Rate Limiting."
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseRateLimiter();

// 4. Проміжне ПЗ (Middleware) для автоматичної трансляції ідентичності (Token Propagation)
app.Use(async (context, next) =>
{
    // Якщо прийшов заголовок Authorization або X-Mock-Role, транслюємо його у внутрішні заголовки
    if (context.Request.Headers.TryGetValue("X-Mock-Role", out var mockRole))
    {
        context.Request.Headers["X-User-Role"] = mockRole;
        context.Request.Headers["X-User-Id"] = "usr_simulated_777";
        context.Request.Headers["X-User-Email"] = "dev@mastery.com";
    }

    if (!context.Request.Headers.ContainsKey("X-Correlation-Id"))
    {
        context.Request.Headers["X-Correlation-Id"] = Guid.NewGuid().ToString("N");
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "YARP Gateway v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseAuthorization();
app.MapControllers();

// 5. Підключення конвеєра проксі-маршрутизації YARP
app.MapReverseProxy();

app.Run();
