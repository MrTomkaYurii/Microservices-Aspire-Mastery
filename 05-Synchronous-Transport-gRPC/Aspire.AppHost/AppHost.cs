var builder = DistributedApplication.CreateBuilder(args);

// =========================================================================
// 1. gRPC Сервер складу Inventory.Grpc
//    Працює по протоколу HTTP/2 з високою швидкістю та бінарним Protobuf
// =========================================================================
var inventoryGrpc = builder.AddProject<Projects.Inventory_Grpc>("inventory-grpc");

// =========================================================================
// 2. Web API замовлень Order.Client.Api
//    Взаємодіє з gRPC сервером через Aspire Service Discovery ("https+http://inventory-grpc")
// =========================================================================
builder.AddProject<Projects.Order_Client_Api>("order-client-api")
    .WithReference(inventoryGrpc)
    .WaitFor(inventoryGrpc);

builder.Build().Run();
