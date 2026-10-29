var builder = DistributedApplication.CreateBuilder(args);

// =========================================================================
// 1. Брокер повідомлень: RabbitMQ з плагіном Management
//    Запускається в контейнері з персистентним томом і веб-панеллю управління
// =========================================================================
var username = builder.AddParameter("username", "guest", secret: true);
var password = builder.AddParameter("password", "guest", secret: true);

var rabbitmq = builder.AddRabbitMQ("messaging", username, password, port: 5672)
    .WithManagementPlugin()
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// =========================================================================
// 2. Sender.Api (Publisher)
//    Надає HTTP ендпоінти для демонстрації Direct, Fanout, Topic exchanges
//    та публікації повідомлень з TTL / Dead-Letter routing.
// =========================================================================
var sender = builder.AddProject<Projects.Sender_Api>("sender-api")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

// =========================================================================
// 3. Receiver.Worker (Consumer)
//    Підписується на створені черги, демонструє ручний ACK/NACK,
//    надійність QoS (Prefetch Count) та обробку Dead-Letter черги.
// =========================================================================
builder.AddProject<Projects.Receiver_Worker>("receiver-worker")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

builder.Build().Run();
