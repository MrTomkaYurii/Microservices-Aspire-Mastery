var builder = DistributedApplication.CreateBuilder(args);

// =========================================================================
// 1. Брокер повідомлень: RabbitMQ для MassTransit шини
// =========================================================================
var username = builder.AddParameter("username", "guest", secret: true);
var password = builder.AddParameter("password", "guest", secret: true);

var rabbitmq = builder.AddRabbitMQ("messaging", username, password, port: 5672)
    .WithManagementPlugin()
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// =========================================================================
// 2. Воркер складу Warehouse.Worker (MassTransit Consumers)
//    Обробляє події OrderCreatedEvent та запити перевірки залишків CheckStockRequest
// =========================================================================
var warehouse = builder.AddProject<Projects.Warehouse_Worker>("warehouse-worker")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

// =========================================================================
// 3. Web API замовлень Order.Api (Publisher & Request Client)
//    Публікує події створення замовлень та виконує синхронні Request/Response
//    запити до складу через шину без блокування HTTP.
// =========================================================================
var orderApi = builder.AddProject<Projects.Order_Api>("order-api")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

builder.Build().Run();
