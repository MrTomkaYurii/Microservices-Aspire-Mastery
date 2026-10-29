using Contracts;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Реєстрація MassTransit для публікації подій та Request/Response клієнта
// =========================================================================
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.UsingRabbitMq((context, cfg) =>
    {
        var connString = context.GetRequiredService<IConfiguration>().GetConnectionString("messaging");
        cfg.Host(connString ?? "amqp://guest:guest@localhost:5672");
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var ordersGroup = app.MapGroup("/api/orders")
    .WithTags("Orders & MassTransit Event-Driven");

// Допоміжний список тестових ID
ordersGroup.MapGet("/sample-products", () => Results.Ok(new[]
{
    new { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Смарт-годинник Pro Pulse (В наявності: 10 шт.)" },
    new { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "Бездротова миша UltraGlide 4K (В наявності: 2 шт.)" }
}))
.WithName("GetSampleProducts")
.WithSummary("Отримати тестові ID товарів для перевірки");

// =========================================================================
// СЦЕНАРІЙ 1: PUBLISH / SUBSCRIBE ПАТЕРН
// =========================================================================
ordersGroup.MapPost("/", async (CreateOrderRequest request, IPublishEndpoint publishEndpoint, ILogger<Program> logger) =>
{
    var orderId = Guid.NewGuid();
    var total = request.UnitPrice * request.Quantity;

    logger.LogInformation("📝 [Order.Api] Створено замовлення {OrderId} для '{Customer}'. Публікуємо OrderCreatedEvent...",
        orderId, request.CustomerName);

    // IPublishEndpoint автоматично визначає тип повідомлення,
    // знаходить або створює відповідний Exchange у RabbitMQ
    // та відправляє подію всім зареєстрованим консьюмерам!
    await publishEndpoint.Publish(new OrderCreatedEvent(
        orderId,
        request.CustomerName,
        request.ProductId,
        request.ProductName,
        total,
        request.Quantity,
        DateTime.UtcNow));

    return Results.Accepted($"/api/orders/{orderId}", new
    {
        Message = "Замовлення прийнято в обробку. Подія OrderCreatedEvent опублікована в MassTransit.",
        OrderId = orderId,
        TotalAmount = total
    });
})
.WithName("CreateOrder")
.WithSummary("Створити замовлення та опублікувати OrderCreatedEvent (Pub/Sub)");

// =========================================================================
// СЦЕНАРІЙ 2: REQUEST / RESPONSE ПАТЕРН ЧЕРЕЗ ШИНУ ПОВІДОМЛЕНЬ
// =========================================================================
ordersGroup.MapPost("/check-stock", async (
    CheckStockRequest request,
    IRequestClient<CheckStockRequest> requestClient,
    ILogger<Program> logger) =>
{
    logger.LogInformation("📡 [Order.Api] Відправляємо синхронний запит CheckStockRequest до складу через шину MassTransit...");

    // IRequestClient відправляє повідомлення у чергу складу,
    // створює тимчасову чергу відповідей і чекає на типизований результат
    // без жодного прямого HTTP-з'єднання з мікросервісом складу!
    var response = await requestClient.GetResponse<StockAvailableResponse, StockUnavailableResponse>(request);

    if (response.Is(out Response<StockAvailableResponse>? available))
    {
        logger.LogInformation("✅ [Order.Api] Отримано відповідь StockAvailableResponse! Код підтвердження: {Code}",
            available.Message.ConfirmationCode);

        return Results.Ok(new
        {
            Status = "InStock",
            available.Message.ProductId,
            available.Message.AvailableQuantity,
            available.Message.UnitPrice,
            available.Message.ConfirmationCode
        });
    }

    if (response.Is(out Response<StockUnavailableResponse>? unavailable))
    {
        logger.LogWarning("⚠️ [Order.Api] Отримано відповідь StockUnavailableResponse: {Reason}",
            unavailable.Message.Reason);

        return Results.Conflict(new
        {
            Status = "OutOfStock",
            unavailable.Message.ProductId,
            unavailable.Message.RequestedQuantity,
            unavailable.Message.CurrentStock,
            unavailable.Message.Reason
        });
    }

    return Results.Problem("Не вдалося визначити статус відповіді від складу.");
})
.WithName("CheckStock")
.WithSummary("Перевірити наявність залишків через MassTransit Request/Response");

app.Run();

public record CreateOrderRequest(
    string CustomerName,
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity);
