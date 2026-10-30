using Basket.Api.Services;
using Microsoft.Extensions.Caching.Hybrid;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. КРОК 1: Локальний In-Memory кеш (RAM поточного процесу)
//    Найпростіший, мікросекундна швидкість, але не синхронізується між копіями.
// =========================================================================
builder.Services.AddMemoryCache();

// =========================================================================
// 3. КРОК 2: Чистий розподілений кеш Redis (IDistributedCache)
//    Спільний для всіх інстансів, але має накладні витрати на TCP та JSON серіалізацію.
// =========================================================================
builder.AddRedisDistributedCache("cache");

// =========================================================================
// 4. КРОК 3: Новий HybridCache у .NET 10 (L1 RAM + L2 Redis)
//    Автоматичний захист від Cache Stampede (блокування фабрики) + теги інвалідації.
// =========================================================================
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    };
});

// =========================================================================
// 5. КРОК 4: Синхронізація між мікросервісами через Redis Pub/Sub
//    - IConnectionMultiplexer: постійне TCP-з'єднання з Redis
//    - PricingUpdateSubscriber: фоновий воркер, що слухає канал 'pricing-channel'
//      через `subscriber.SubscribeAsync(...)` і скидає кеш при зміні цін
// =========================================================================
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("cache") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
});

builder.Services.AddSingleton<BasketStore>();
builder.Services.AddHostedService<PricingUpdateSubscriber>();

// =========================================================================
// 6. Реєстрація покрокових MVC контролерів та Swagger документації
// =========================================================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Basket API - Покроковий майстер-клас із кешування",
        Version = "v1",
        Description = "Наочний посібник: від найпростішого In-Memory кешу (Крок 1) до чистого Redis (Крок 2), HybridCache (Крок 3) та міжсервісної інвалідації через Redis Pub/Sub (Крок 4)."
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Caching Masterclass v1");
        c.RoutePrefix = string.Empty; // Swagger доступний одразу в корені
    });

    app.MapGet("/swagger", () => Results.Redirect("/"));
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
