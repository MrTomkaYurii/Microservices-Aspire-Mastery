using System.Text.Json;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Redis ConnectionMultiplexer для публікації повідомлень у Pub/Sub
// =========================================================================
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("cache") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
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

// Демо-довідник актуальних цін
var activePrices = new Dictionary<Guid, (string Name, decimal Price)>
{
    [Guid.Parse("11111111-1111-1111-1111-111111111111")] = ("Смарт-годинник Pro Pulse", 199.99m),
    [Guid.Parse("22222222-2222-2222-2222-222222222222")] = ("Бездротова миша UltraGlide 4K", 79.99m)
};
decimal currentDiscountPercent = 0m;

// =========================================================================
// 3. Ендпоінти управління цінами та ініціації Cache Invalidation
// =========================================================================
var pricingGroup = app.MapGroup("/api/pricing")
    .WithTags("Pricing & Discounts");

// Отримати поточні ціни та знижки
pricingGroup.MapGet("/", () =>
{
    var result = activePrices.Select(p => new
    {
        ProductId = p.Key,
        ProductName = p.Value.Name,
        CurrentPrice = p.Value.Price
    }).ToList();

    return Results.Ok(new
    {
        DiscountPercent = currentDiscountPercent,
        Products = result
    });
})
.WithName("GetPricingList")
.WithSummary("Отримати перелік актуальних цін та активну знижку");

// Змінити ціну на товар (провокує інвалідацію кешу в Basket.Api)
pricingGroup.MapPost("/update-price", async (UpdatePriceRequest request, IConnectionMultiplexer redis, ILogger<Program> logger) =>
{
    if (!activePrices.ContainsKey(request.ProductId))
    {
        return Results.NotFound($"Товар з ID {request.ProductId} не знайдено в каталозі цін.");
    }

    var oldPrice = activePrices[request.ProductId].Price;
    activePrices[request.ProductId] = (activePrices[request.ProductId].Name, request.NewPrice);

    logger.LogInformation("🏷️ [Pricing.Api] Зміна ціни для {ProductId}: {OldPrice:C} -> {NewPrice:C} (Причина: {Reason})",
        request.ProductId, oldPrice, request.NewPrice, request.Reason);

    // Відправляємо подію інвалідації в Redis Pub/Sub канал 'pricing-channel'
    var eventPayload = new
    {
        EventType = "PriceUpdatedEvent",
        request.ProductId,
        request.NewPrice,
        request.Reason,
        TimestampUtc = DateTime.UtcNow
    };

    var subscriber = redis.GetSubscriber();
    await subscriber.PublishAsync(RedisChannel.Literal("pricing-channel"), JsonSerializer.Serialize(eventPayload));

    logger.LogInformation("📢 [Pricing.Api] Сигнал інвалідації відправлено в Redis Pub/Sub канал 'pricing-channel'!");

    return Results.Ok(new
    {
        Message = "Ціну успішно оновлено. Подія інвалідації кешу розіслана через Redis Pub/Sub.",
        request.ProductId,
        OldPrice = oldPrice,
        request.NewPrice
    });
})
.WithName("UpdateProductPrice")
.WithSummary("Оновити ціну товару та відправити сигнал інвалідації кешу кошиків");

// Застосувати глобальну знижку / промокод
pricingGroup.MapPost("/apply-discount", async (ApplyDiscountRequest request, IConnectionMultiplexer redis, ILogger<Program> logger) =>
{
    currentDiscountPercent = request.DiscountPercent;

    logger.LogInformation("🎁 [Pricing.Api] Застосовано промокод '{Code}': знижка {Percent}%",
        request.DiscountCode, request.DiscountPercent);

    var eventPayload = new
    {
        EventType = "DiscountAppliedEvent",
        request.DiscountCode,
        request.DiscountPercent,
        TimestampUtc = DateTime.UtcNow
    };

    var subscriber = redis.GetSubscriber();
    await subscriber.PublishAsync(RedisChannel.Literal("pricing-channel"), JsonSerializer.Serialize(eventPayload));

    logger.LogInformation("📢 [Pricing.Api] Сигнал зміни знижки відправлено в Redis Pub/Sub!");

    return Results.Ok(new
    {
        Message = $"Знижку {request.DiscountPercent}% успішно застосовано. Кеш кошиків інвалідовано для перерахунку рахунку.",
        request.DiscountCode,
        DiscountPercent = request.DiscountPercent
    });
})
.WithName("ApplyDiscount")
.WithSummary("Застосувати промокод і змусити кошики перерахувати суму");

app.Run();

public record UpdatePriceRequest(Guid ProductId, decimal NewPrice, string Reason);
public record ApplyDiscountRequest(string DiscountCode, decimal DiscountPercent);
