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
        Expiration = TimeSpan.FromMinutes(10),          // Час життя у Redis (L2)
        LocalCacheExpiration = TimeSpan.FromMinutes(2)  // Час життя у пам'яті процесу (L1)
    };
});

// Сховище даних та фоновий слухач інвалідації кешу через Redis Pub/Sub
builder.Services.AddSingleton<BasketStore>();
builder.Services.AddHostedService<PricingUpdateSubscriber>();

// =========================================================================
// 4. Реєстрація MVC контролерів та Swagger
// =========================================================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Basket API - Redis HybridCache",
        Version = "v1",
        Description = "Мікросервіс кошика з багаторівневим кешуванням HybridCache (L1 Memory + L2 Redis), захистом від Cache Stampede та контролерами ASP.NET Core."
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Basket API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
