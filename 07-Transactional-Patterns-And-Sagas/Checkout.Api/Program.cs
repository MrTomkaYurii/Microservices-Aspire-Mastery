using Checkout.Api.BackgroundServices;
using Checkout.Api.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================================
// 🎯 МОДУЛЬ 07: CHECKOUT.API - ТРАНЗАКЦІЙНІ ПАТЕРНИ, OUTBOX ТА САГИ
// =========================================================================================

// 1. Стандартні налаштування .NET Aspire
builder.AddServiceDefaults();

// 2. База даних PostgreSQL (з фолбеком на InMemory для легкого тестування)
var postgresConnection = builder.Configuration.GetConnectionString("checkoutdb");
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(postgresConnection))
    {
        options.UseNpgsql(postgresConnection);
    }
    else
    {
        options.UseInMemoryDatabase("CheckoutDbInMemory");
    }
});

// 3. Налаштування MassTransit з нативним Entity Framework Core Outbox
builder.Services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitConnection = builder.Configuration.GetConnectionString("eventbus");
        if (!string.IsNullOrWhiteSpace(rabbitConnection))
        {
            cfg.Host(rabbitConnection);
        }
        else
        {
            cfg.Host("localhost", "/", h =>
            {
                h.Username("guest");
                h.Password("guest");
            });
        }

        cfg.ConfigureEndpoints(context);
    });
});

// 4. Реєстрація кастомного OutboxPublisherWorker з Polly v8 (для вивчення Кроку 2)
builder.Services.AddHostedService<OutboxPublisherWorker>();

// 5. Підключення класичних контролерів та Swagger UI
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Checkout.Api - Transactional Patterns & Sagas (.NET 10)",
        Version = "v1",
        Description = "Покроковий модуль для вивчення Dual-Write Problem, Transactional Outbox з Polly v8, MassTransit Outbox та Saga Compensations."
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

// Автоматичне застосування міграцій при запуску
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
    {
        db.Database.EnsureCreated();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Checkout.Api v1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseAuthorization();
app.MapControllers();

app.Run();
