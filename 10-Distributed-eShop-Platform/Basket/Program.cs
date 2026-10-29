using System.Reflection;
using Basket.Data;
using Basket.Data.Processors;
using Basket.Endpoints;
using Basket.Services;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================================
// 🎯 МОДУЛЬ 10: BASKET SERVICE - КОШИК ПОКУПЦЯ (REDIS HYBRIDCACHE + OUTBOX)
// =========================================================================================

builder.AddServiceDefaults();

// 1. База даних PostgreSQL для персистентного кошика
builder.AddNpgsqlDbContext<BasketDbContext>(connectionName: "basketdb");

// 2. Redis + HybridCache (.NET 10)
builder.AddRedisDistributedCache(connectionName: "cache");
builder.Services.AddHybridCache(options =>
{    
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };
});

builder.Services.AddScoped<BasketService>();

// 3. Клієнт зв'язку з Catalog через Aspire Service Discovery
builder.Services.AddHttpClient<CatalogApiClient>(client =>
{
    client.BaseAddress = new("https+http://catalog");
});

// 4. Підключення RabbitMQ.Client v7
builder.Services.AddSingleton<IConnection>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var rabbitConnectionString = config.GetConnectionString("rabbitmq");

    var factory = new ConnectionFactory
    {
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
    };

    if (!string.IsNullOrWhiteSpace(rabbitConnectionString))
    {
        factory.Uri = new Uri(rabbitConnectionString);
    }
    else
    {
        factory.HostName = "localhost";
        factory.Port = 5672;
        factory.UserName = "guest";
        factory.Password = "guest";
    }

    try
    {
        return factory.CreateConnectionAsync().GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException("Failed to connect to RabbitMQ broker", ex);
    }
});

// 5. MassTransit та OutboxProcessor
builder.Services.AddMassTransitWithAssemblies(Assembly.GetExecutingAssembly());
builder.Services.AddHostedService<OutboxProcessor>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseHttpsRedirection();
app.UseMigration();
app.MapBasketEndpoints();

app.Run();
