using System.Reflection;
using Microsoft.Extensions.Hosting;
using Ordering.Data;
using Ordering.EventHandlers;
using Ordering.Services;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================================
// 🎯 МОДУЛЬ 10: ORDERING SERVICE - СЕРВІС ЗАМОВЛЕНЬ У СИСТЕМІ ESHOP
// =========================================================================================

// 1. Стандартні налаштування Aspire (OpenTelemetry, HealthChecks, Service Discovery)
builder.AddServiceDefaults();

// 2. База даних SQL Server для зберігання замовлень
builder.AddSqlServerDbContext<OrderDbContext>(connectionName: "orderdb");

builder.Services.AddScoped<OrderService>();

// 3. Підключення RabbitMQ.Client v7 через Aspire Connection String
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

// Фоновий обробник подій кошика безпосередньо через RabbitMQ.Client
builder.Services.AddHostedService<BasketCheckoutEventHandlerWithoutMassTransit>();

// MassTransit шина для подій та Outbox
builder.Services.AddMassTransitWithAssemblies(Assembly.GetExecutingAssembly());

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseHttpsRedirection();
app.UseMigration();

app.MapOrderEndpoints();

app.Run();
