using Inventory.Grpc.Services;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Реєстрація gRPC сервера та діагностичного Swagger UI
// =========================================================================
builder.Services.AddGrpc();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Inventory.Grpc Server Diagnostics (.NET 10 & HTTP/2)",
        Version = "v1",
        Description = "Діагностичний інтерфейс gRPC-сервера складу. Основні бінарні виклики (Unary та Server Streaming) приймаються по HTTP/2 через InventoryGrpcServiceImpl."
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Inventory.Grpc Diagnostics v1");
        c.RoutePrefix = string.Empty; // Swagger відкривається одразу в корені
    });

    app.MapGet("/swagger", () => Results.Redirect("/"));
}

// Реєструємо бінарний сервіс gRPC та діагностичні контролери
app.MapGrpcService<InventoryGrpcServiceImpl>();
app.MapControllers();

app.Run();
