
using Microsoft.Extensions.Hosting;
using Ordering.EventHandlers;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.AddSqlServerDbContext<OrderDbContext>(connectionName: "orderdb");

builder.Services.AddScoped<OrderService>(); // зареєстрований як скоуп-сервіс





//-----------------------------------------------------------------------------------------------
// налаштування RabbitMQ.Client через конфігурацію
builder.Services.AddSingleton<global::RabbitMQ.Client.IConnection>(sp =>
{
    var factory = new global::RabbitMQ.Client.ConnectionFactory
    {
        HostName = "localhost",             // адрес контейнера
        Port = 5672,                        // порт RabbitMQ
        UserName = "tomkarabbit",          // логин
        Password = "tomkarabbit",          // пароль
        AutomaticRecoveryEnabled = true,    // авто-восстановление соединения
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
    };
    try
    {
        global::RabbitMQ.Client.IConnection connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
        Console.WriteLine("[RabbitMQ] Connection established successfully.");
        return connection;
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException("Failed to connect to RabbitMQ", ex);
    }
});
//Реєстрація обробника подій як хостинг-сервісу ЗАВЖДИ СІНГЛТОН
builder.Services.AddHostedService<BasketCheckoutEventHandlerWithoutMassTransit>();
//-----------------------------------------------------------------------------------------------
//якщо з MassTransit (автоматична реєстрація всіх консьюемрів)
builder.Services.AddMassTransitWithAssemblies(Assembly.GetExecutingAssembly());
//-----------------------------------------------------------------------------------------------





var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapDefaultEndpoints();

app.UseHttpsRedirection();

app.UseMigration();

app.MapOrderEndpoints();

app.Run();
