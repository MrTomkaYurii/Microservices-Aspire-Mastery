using Inventory.Grpc.Protos;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Реєстрація типізованого gRPC клієнта з Aspire Service Discovery
//    "https+http://inventory-grpc" автоматично транслюється у реальний порт сервера!
// =========================================================================
builder.Services.AddGrpcClient<InventoryService.InventoryServiceClient>(o =>
{
    o.Address = new Uri("https+http://inventory-grpc");
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
        Title = "Order Client API - Synchronous gRPC Transport",
        Version = "v1",
        Description = "API-шлюз з повноцінними контролерами ASP.NET Core, що викликає gRPC сервер (Unary + Server Streaming) через Aspire Service Discovery."
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order Client API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
