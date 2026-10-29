using System.Diagnostics;
using Grpc.Core;
using Inventory.Grpc.Protos;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Реєстрація типізованого gRPC клієнта з Aspire Service Discovery
//    "https+http://inventory-grpc" автоматично транслюється у реальний порт сервера!
// =========================================================================
builder.Services.AddGrpcClient<InventoryService.InventoryServiceClient>(o =>
{
    o.Address = new Uri("https+http://inventory-grpc");
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

var orderGroup = app.MapGroup("/api/orders")
    .WithTags("gRPC Client & Fast Synchronous RPC");

// =========================================================================
// 1. UNARY RPC: Швидка перевірка залишку через gRPC
// =========================================================================
orderGroup.MapGet("/check-stock/{productId}", async (
    string productId,
    InventoryService.InventoryServiceClient client,
    ILogger<Program> logger) =>
{
    var sw = Stopwatch.StartNew();
    logger.LogInformation("📞 [Order.Client] Виклик gRPC GetProductStock для {ProductId}...", productId);

    var response = await client.GetProductStockAsync(new ProductStockRequest
    {
        ProductId = productId
    });

    sw.Stop();
    logger.LogInformation("⚡ [Order.Client] Відповідь отримана за {ElapsedMs}ms: {Product}, Наявно: {Stock}, Ціна: {Price:C}",
        sw.ElapsedMilliseconds, response.ProductName, response.AvailableQuantity, response.UnitPrice);

    return Results.Ok(new
    {
        RoundTripTimeMs = sw.ElapsedMilliseconds,
        response.ProductId,
        response.ProductName,
        response.AvailableQuantity,
        response.UnitPrice,
        response.InStock
    });
})
.WithName("CheckStockViaGrpc")
.WithSummary("Синхронний швидкий виклик gRPC GetProductStock через Service Discovery");

// =========================================================================
// 2. UNARY RPC: Резервування товару на складі через gRPC
// =========================================================================
orderGroup.MapPost("/reserve", async (
    ReserveItemRequest request,
    InventoryService.InventoryServiceClient client,
    ILogger<Program> logger) =>
{
    logger.LogInformation("🔒 [Order.Client] Виклик gRPC ReserveStock для {ProductId}, кількість: {Qty}...",
        request.ProductId, request.Quantity);

    var response = await client.ReserveStockAsync(new ReserveStockRequest
    {
        ProductId = request.ProductId,
        Quantity = request.Quantity,
        OrderId = Guid.NewGuid().ToString()
    });

    if (response.Success)
    {
        logger.LogInformation("✅ [Order.Client] Успішна резервація! Код: {ResId}, Залишок: {Rem}",
            response.ReservationId, response.RemainingStock);

        return Results.Ok(new
        {
            response.Success,
            response.Message,
            response.ReservationId,
            response.RemainingStock
        });
    }

    logger.LogWarning("⚠️ [Order.Client] Відмова у резервації: {Message}", response.Message);
    return Results.Conflict(new
    {
        response.Success,
        response.Message,
        response.RemainingStock
    });
})
.WithName("ReserveStockViaGrpc")
.WithSummary("Атомарне резервування залишків товару через gRPC");

// =========================================================================
// 3. SERVER STREAMING: Отримання потоку залишків у реальному часі
// =========================================================================
orderGroup.MapPost("/stream-stock", async (
    StockStreamInput request,
    InventoryService.InventoryServiceClient client,
    ILogger<Program> logger) =>
{
    logger.LogInformation("🌊 [Order.Client] Запит серверного стрімінгу для {Count} товарів...", request.ProductIds.Length);

    var rpcRequest = new StockStreamRequest();
    rpcRequest.ProductIds.AddRange(request.ProductIds);

    using var streamingCall = client.GetStockStream(rpcRequest);
    var results = new List<object>();

    await foreach (var item in streamingCall.ResponseStream.ReadAllAsync())
    {
        results.Add(new
        {
            item.ProductId,
            item.ProductName,
            item.AvailableQuantity,
            item.UnitPrice,
            item.InStock
        });

        logger.LogInformation("📦 [Order.Client] Стрім доставив статус: {Name} ({Qty} шт.)", item.ProductName, item.AvailableQuantity);
    }

    return Results.Ok(new
    {
        StreamedItemsCount = results.Count,
        Items = results
    });
})
.WithName("StreamStockViaGrpc")
.WithSummary("Серверний стрімінг gRPC: отримання потоку статусів товарів");

app.Run();

public record ReserveItemRequest(string ProductId, int Quantity);
public record StockStreamInput(string[] ProductIds);
