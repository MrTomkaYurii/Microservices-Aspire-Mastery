using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Warehouse.Worker.Consumers;

var builder = Host.CreateApplicationBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія OpenTelemetry, HealthChecks)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Конфігурація шини MassTransit поверх RabbitMQ
// =========================================================================
builder.Services.AddMassTransit(x =>
{
    // Стандарт іменування черг у стилі kebab-case (наприклад: "order-created", "check-stock")
    x.SetKebabCaseEndpointNameFormatter();

    // Реєстрація консьюмерів
    x.AddConsumer<OrderCreatedConsumer>();
    x.AddConsumer<CheckStockConsumer>();
    x.AddConsumer<OrderCreatedFaultConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var connString = context.GetRequiredService<IConfiguration>().GetConnectionString("messaging");
        cfg.Host(connString ?? "amqp://guest:guest@localhost:5672");

        // ПОЛІТИКА ПОВТОРНИХ СПРОБ (RETRY POLICY):
        // При помилці MassTransit робить 3 повторні спроби з інтервалом 2 секунди.
        // Якщо всі спроби проваляться, повідомлення автоматично переміститься
        // у чергу помилок [queue-name]_error!
        cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(2)));

        // Автоматично налаштовує ендпоінти для всіх зареєстрованих консьюмерів
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
