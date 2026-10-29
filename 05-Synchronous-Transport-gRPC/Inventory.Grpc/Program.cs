using Inventory.Grpc.Services;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Реєстрація gRPC сервера
// =========================================================================
builder.Services.AddGrpc();

var app = builder.Build();

app.MapDefaultEndpoints();

// Реєструємо сервіс gRPC
app.MapGrpcService<InventoryGrpcServiceImpl>();

app.MapGet("/", () => "Inventory.Grpc Server (.NET 10 & HTTP/2). Використовуйте gRPC клієнт для виклику сервісу.");

app.Run();
