using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Redis ConnectionMultiplexer для публікації повідомлень у Pub/Sub
// =========================================================================
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("cache") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
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
        Title = "Pricing API - Cache Invalidation Producer",
        Version = "v1",
        Description = "Мікросервіс управління цінами та ініціації міжсервісної інвалідації через Redis Pub/Sub."
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pricing API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
