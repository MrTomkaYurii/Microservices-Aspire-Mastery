using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Реєстрація з'єднання RabbitMQ v7 (IConnection Singleton)
//    Підключення "messaging" автоматично налаштовується .NET Aspire
// =========================================================================
builder.Services.AddSingleton<IConnection>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connString = config.GetConnectionString("messaging");

    var factory = new ConnectionFactory
    {
        Uri = new Uri(connString ?? "amqp://guest:guest@localhost:5672"),
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
    };

    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
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
        Title = "Sender API - RabbitMQ Low-Level Mechanics",
        Version = "v1",
        Description = "Продюсер низькорівневих повідомлень RabbitMQ (Direct, Fanout, Topic, DLX) з використанням ASP.NET Core контролерів."
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "RabbitMQ Sender API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
