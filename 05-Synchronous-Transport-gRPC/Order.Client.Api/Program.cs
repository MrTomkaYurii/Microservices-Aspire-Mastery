using Inventory.Grpc.Protos;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Реєстрація gRPC клієнта з Aspire Service Discovery
//    Зверніть увагу: ми використовуємо логічне ім'я "inventory-grpc",
//    яке Aspire автоматично резолвить у реальний endpoint (HTTP/2) без хардкоду портів!
// =========================================================================
builder.Services.AddGrpcClient<InventoryService.InventoryServiceClient>(o =>
{
    o.Address = new("https+http://inventory-grpc");
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
        Title = "Order Service API (gRPC Client vs REST)",
        Version = "v1",
        Description = "Клієнтський мікросервіс замовлень, який взаємодіє з Inventory Service через високопродуктивний бінарний gRPC зв'язок на основі Service Discovery."
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
        c.RoutePrefix = string.Empty; // Swagger доступний у корені
    });

    app.MapGet("/swagger", () => Results.Redirect("/"));
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
