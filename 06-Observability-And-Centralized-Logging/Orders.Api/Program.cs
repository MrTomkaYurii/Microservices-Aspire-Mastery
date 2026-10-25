using Orders.Api.Health;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================================
// 🎯 МОДУЛЬ 06: ORDERS.API - ЦЕНТРАЛІЗОВАНА СПОСТЕРЕЖУВАНІСТЬ ТА СЕРИЛОГ
// =========================================================================================

// 1. Налаштування Serilog із виведенням у Консоль та централізований Seq
var seqUrl = builder.Configuration.GetConnectionString("seq") ?? "http://localhost:5341";

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Orders.Api")
        .WriteTo.Console()
        .WriteTo.Seq(seqUrl);
});

// 2. Стандартні налаштування Aspire (OpenTelemetry Tracing, Metrics, Service Discovery, Health Checks)
builder.AddServiceDefaults();

// 3. Реєстрація кастомного перевірки здоров'я
builder.Services.AddHealthChecks()
    .AddCheck<PaymentGatewayHealthCheck>("payment_gateway", tags: new[] { "ready" });

// 4. Підключення класичних контролерів та Swagger UI
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Orders.Api - Observability & Logging (.NET 10)",
        Version = "v1",
        Description = "Покроковий модуль для вивчення Health Checks, OpenTelemetry Tracing, Metrics та Serilog+Seq."
    });
});

var app = builder.Build();

// Ендпоінти Aspire за замовчуванням (/health та /alive)
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Orders.Api v1");
        c.RoutePrefix = string.Empty; // Swagger доступний у корені http://localhost:порт/
    });
}

app.UseAuthorization();
app.MapControllers();

app.Run();
