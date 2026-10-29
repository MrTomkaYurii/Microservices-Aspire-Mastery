using Contracts;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Налаштування MassTransit з RabbitMQ
// =========================================================================
builder.Services.AddMassTransit(x =>
{
    // Реєструємо клієнт для патерну Request/Response
    x.AddRequestClient<CheckStockRequest>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var config = context.GetRequiredService<IConfiguration>();
        var connString = config.GetConnectionString("messaging");

        cfg.Host(connString ?? "amqp://guest:guest@localhost:5672");
        cfg.ConfigureEndpoints(context);
    });
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
        Title = "Order API - MassTransit Event-Driven Architecture",
        Version = "v1",
        Description = "Мікросервіс замовлень з контролерами ASP.NET Core, що публікує події та надсилає запити у шину MassTransit."
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
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
