using Basket.Api.Models;
using Basket.Api.Services;
using Microsoft.Extensions.Caching.Hybrid;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Підключення Redis Distributed Cache через Aspire Component
// =========================================================================
builder.AddRedisDistributedCache("cache");

// Реєструємо IConnectionMultiplexer для Pub/Sub повідомлень
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("cache") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
});

// =========================================================================
// 3. Сучасний .NET 10 HybridCache (L1 Memory + L2 Redis)
// =========================================================================
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(10),       // Час життя у Redis (L2)
        LocalCacheExpiration = TimeSpan.FromMinutes(2) // Час життя у локальній пам'яті процесу (L1)
    };
});

// Сховище даних та фоновий слухач інвалідації кэшу
builder.Services.AddSingleton<BasketStore>();
builder.Services.AddHostedService<PricingUpdateSubscriber>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// =========================================================================
// 4. REST API кошика з демонстрацією HybridCache (Cache-Aside + Invalidation)
// =========================================================================
var basketGroup = app.MapGroup("/api/basket")
    .WithTags("Basket");

// Отримати кошик користувача
basketGroup.MapGet("/{userName}", async (string userName, HybridCache cache, BasketStore store, ILogger<Program> logger) =>
{
    var cacheKey = $"basket:{userName.ToLowerInvariant()}";

    // GetOrCreateAsync захищає від Cache Stampede:
    // якщо 100 користувачів одночасно запросять кошик, виконається лише 1 звернення до сховища!
    var cart = await cache.GetOrCreateAsync(
        cacheKey,
        async cancel =>
        {
            logger.LogWarning("🔍 [Cache MISS] Кошик для '{User}' не знайдено в HybridCache. Завантажуємо зі сховища...", userName);
            return await store.GetAsync(userName);
        },
        tags: ["baskets", $"user:{userName}"]);

    if (cart is null)
    {
        return Results.NotFound($"Кошик для користувача '{userName}' порожній або не існує.");
    }

    logger.LogInformation("⚡ [Cache HIT] Кошик для '{User}' повернуто з HybridCache. Сума до сплати: {Total:C}", userName, cart.TotalPrice);
    return Results.Ok(cart);
})
.WithName("GetBasket")
.WithSummary("Отримати кошик з HybridCache (або завантажити зі сховища при Cache Miss)");

// Оновити або створити кошик
basketGroup.MapPost("/", async (ShoppingCart cart, HybridCache cache, BasketStore store, ILogger<Program> logger) =>
{
    await store.SaveAsync(cart);

    var cacheKey = $"basket:{cart.UserName.ToLowerInvariant()}";
    await cache.SetAsync(cacheKey, cart, tags: ["baskets", $"user:{cart.UserName}"]);

    logger.LogInformation("💾 [Cache UPDATE] Кошик для '{User}' збережено та оновлено в HybridCache (L1 + L2).", cart.UserName);
    return Results.Ok(cart);
})
.WithName("UpdateBasket")
.WithSummary("Зберегти кошик у сховищі та записати в HybridCache");

// Очистити кошик
basketGroup.MapDelete("/{userName}", async (string userName, HybridCache cache, BasketStore store, ILogger<Program> logger) =>
{
    await store.DeleteAsync(userName);
    await cache.RemoveAsync($"basket:{userName.ToLowerInvariant()}");

    logger.LogInformation("🗑️ [Cache REMOVE] Кошик для '{User}' видалено зі сховища та очищено з кешу.", userName);
    return Results.NoContent();
})
.WithName("DeleteBasket")
.WithSummary("Видалити кошик та інвалідувати ключ у кеші");

app.Run();
