
using Microsoft.Extensions.Hosting;

using static MassTransit.Logging.DiagnosticHeaders.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<BasketDbContext>(connectionName: "basketdb");

builder.AddRedisDistributedCache(connectionName: "cache");

builder.Services.AddHybridCache(options =>
{    
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1),
        Flags = HybridCacheEntryFlags.DisableLocalCache
    };
});

builder.Services.AddScoped<BasketService>();

builder.Services.AddHttpClient<CatalogApiClient>(client =>
{
    client.BaseAddress = new("https+http://catalog");
});






// налаштування RabbitMQ.Client через конфігурацію
builder.Services.AddSingleton<global::RabbitMQ.Client.IConnection>(sp =>
{
    var factory = new global::RabbitMQ.Client.ConnectionFactory
    {
        HostName = "localhost",             
        Port = 5672,                          
        UserName = "tomkarabbit",            
        Password = "tomkarabbit",            
        AutomaticRecoveryEnabled = true,    
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

// MassTransit 
builder.Services.AddMassTransitWithAssemblies(Assembly.GetExecutingAssembly());
// MassTransit [outbox pattern]
builder.Services.AddHostedService<OutboxProcessor>();





var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapDefaultEndpoints();

app.UseHttpsRedirection();

app.UseMigration();

app.MapBasketEndpoints();

app.Run();
