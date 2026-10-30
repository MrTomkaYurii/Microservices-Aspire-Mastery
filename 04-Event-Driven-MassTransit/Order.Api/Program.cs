using Contracts;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Налаштування шини MassTransit через брокер RabbitMQ
// =========================================================================
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.UsingRabbitMq((context, cfg) =>
    {
        var config = context.GetRequiredService<IConfiguration>();
        var connectionString = config.GetConnectionString("eventbus") ?? "amqp://guest:guest@localhost:5672";
        cfg.Host(new Uri(connectionString));

        cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(2)));

        cfg.ConfigureEndpoints(context);
    });

    x.AddRequestClient<CheckStockRequest>();
});

// =========================================================================
// 3. Реєстрація MVC контролерів та Swagger
// =========================================================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Order Service API (MassTransit Pub/Sub & Request/Response)",
        Version = "v1",
        Description = "Сервіс управління замовленнями з демонстрацією асинхронних патернів MassTransit: Pub/Sub подій та синхронного Request/Response."
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order API v1");
        c.RoutePrefix = string.Empty; // Swagger доступний у корені
    });

    app.MapGet("/swagger", () => Results.Redirect("/"));
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
