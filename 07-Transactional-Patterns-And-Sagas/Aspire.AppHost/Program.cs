var builder = DistributedApplication.CreateBuilder(args);

// =========================================================================================
// 🎯 МОДУЛЬ 07: ОРКЕСТРАЦІЯ ТРАНЗАКЦІЙНИХ ПАТЕРНІВ ТА САГ (OUTBOX & SAGAS)
// =========================================================================================

// 1. Інфраструктура бази даних PostgreSQL
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var orderDb = postgres.AddDatabase("checkoutdb");

// 2. Брокер повідомлень RabbitMQ
var rabbitmq = builder.AddRabbitMQ("eventbus")
    .WithManagementPlugin();

// 3. Сервіс замовлень Checkout.Api:
//    Містить покрокові контролери для вивчення Dual-Write, Custom Outbox з Polly v8,
//    MassTransit Native Outbox та Saga State Machine.
builder.AddProject<Projects.Checkout_Api>("checkout-api")
    .WithReference(orderDb)
    .WithReference(rabbitmq)
    .WaitFor(orderDb)
    .WaitFor(rabbitmq);

builder.Build().Run();
