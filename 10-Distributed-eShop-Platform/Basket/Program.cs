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

// 6. Контролери та Swagger UI
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Basket API - eShop Platform (.NET 10)",
        Version = "v1",
        Description = "Сервіс кошика покупок eShop з Redis HybridCache, PostgreSQL та Outbox Processor."
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
        c.RoutePrefix = string.Empty;
    });

    app.MapGet("/swagger", () => Results.Redirect("/"));
}

app.UseHttpsRedirection();
app.UseMigration();

app.MapControllers();
app.MapBasketEndpoints(); // Збережено для сумісності

app.Run();
