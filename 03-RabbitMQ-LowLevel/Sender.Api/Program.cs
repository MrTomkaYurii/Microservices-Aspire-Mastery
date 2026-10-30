using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (OpenTelemetry, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Підключення до брокера RabbitMQ через свіжий RabbitMQ.Client v7
//    Використовуємо IConnectionFactory з повністю асинхронним API (IChannel)
// =========================================================================
builder.Services.AddSingleton<IConnectionFactory>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("eventbus") ?? "amqp://guest:guest@localhost:5672";
    return new ConnectionFactory
    {
        Uri = new Uri(connectionString),
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
    };
});

// Довгоживуче з'єднання (IConnection)
builder.Services.AddSingleton<IConnection>(sp =>
{
    var factory = sp.GetRequiredService<IConnectionFactory>();
    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

// =========================================================================
// 3. Реєстрація контролерів та Swagger документації
// =========================================================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "RabbitMQ Low-Level Producer API (RabbitMQ.Client v7)",
        Version = "v1",
        Description = "Покроковий курс із низькорівневих топологій RabbitMQ: Direct Exchange, Fanout Broadcast, Topic Wildcards та Dead Letter Exchange (DLX)."
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "RabbitMQ Low-Level API v1");
        c.RoutePrefix = string.Empty; // Swagger доступний у корені
    });

    app.MapGet("/swagger", () => Results.Redirect("/"));
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
