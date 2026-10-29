using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (Телеметрія, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Реєстрація з'єднання RabbitMQ v7 (IConnection)
//    Підключення "messaging" надається оркестратором .NET Aspire!
//    ВАЖЛИВО: IConnection у RabbitMQ довгоживуче (Singleton) і потокобезпечне,
//    але канали (IChannel) створюються під операції / запити.
// =========================================================================
builder.Services.AddSingleton<IConnection>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connString = config.GetConnectionString("messaging");

    var factory = new ConnectionFactory
    {
        Uri = new Uri(connString ?? "amqp://guest:guest@localhost:5672"),
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
    };

    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var rabbitGroup = app.MapGroup("/api/messages")
    .WithTags("RabbitMQ Low-Level Mechanics");

// =========================================================================
// 1. DIRECT EXCHANGE: Маршрутизація за точним співпадінням Routing Key
// =========================================================================
rabbitGroup.MapPost("/direct", async (DirectMessageRequest request, IConnection connection, ILogger<Program> logger) =>
{
    // У RabbitMQ v7 канали створюються асинхронно через CreateChannelAsync
    await using var channel = await connection.CreateChannelAsync();

    // Оголошуємо Direct Exchange (durable = true, щоб зберігся після рестарту брокера)
    await channel.ExchangeDeclareAsync(
        exchange: "direct-exchange",
        type: ExchangeType.Direct,
        durable: true,
        autoDelete: false);

    var payload = JsonSerializer.Serialize(new
    {
        request.MessageId,
        request.Payload,
        DispatchedAtUtc = DateTime.UtcNow
    });
    var body = Encoding.UTF8.GetBytes(payload);

    var props = new BasicProperties
    {
        ContentType = "application/json",
        DeliveryMode = DeliveryModes.Persistent, // Зберігати на диск у разі збою
        MessageId = request.MessageId.ToString(),
        Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
    };

    // Публікуємо повідомлення з конкретним RoutingKey (наприклад: "orders.critical" або "orders.regular")
    await channel.BasicPublishAsync(
        exchange: "direct-exchange",
        routingKey: request.RoutingKey,
        mandatory: true,
        basicProperties: props,
        body: body);

    logger.LogInformation("📤 [Direct] Відправлено в 'direct-exchange' з RoutingKey '{Key}': {Payload}",
        request.RoutingKey, request.Payload);

    return Results.Ok(new { Status = "Dispatched", Exchange = "direct-exchange", request.RoutingKey, request.Payload });
})
.WithName("SendDirectMessage")
.WithSummary("Відправити повідомлення у Direct Exchange (маршрутизація за точним ключем)");

// =========================================================================
// 2. FANOUT EXCHANGE: Широкомовна розсилка всім підписаним чергам
// =========================================================================
rabbitGroup.MapPost("/fanout", async (FanoutMessageRequest request, IConnection connection, ILogger<Program> logger) =>
{
    await using var channel = await connection.CreateChannelAsync();

    await channel.ExchangeDeclareAsync(
        exchange: "fanout-exchange",
        type: ExchangeType.Fanout,
        durable: true,
        autoDelete: false);

    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

    var props = new BasicProperties
    {
        ContentType = "application/json",
        DeliveryMode = DeliveryModes.Persistent
    };

    // У Fanout exchange routingKey ігнорується брокером повністю
    await channel.BasicPublishAsync(
        exchange: "fanout-exchange",
        routingKey: string.Empty,
        mandatory: false,
        basicProperties: props,
        body: body);

    logger.LogInformation("📢 [Fanout] Широкомовне сповіщення відправлено в 'fanout-exchange': {Text}", request.BroadcastText);

    return Results.Ok(new { Status = "Broadcasted", Exchange = "fanout-exchange", request.BroadcastText });
})
.WithName("SendFanoutMessage")
.WithSummary("Відправити повідомлення у Fanout Exchange (усі черги отримають копію)");

// =========================================================================
// 3. TOPIC EXCHANGE: Маршрутизація за масками та шаблонами (* та #)
// =========================================================================
rabbitGroup.MapPost("/topic", async (TopicMessageRequest request, IConnection connection, ILogger<Program> logger) =>
{
    await using var channel = await connection.CreateChannelAsync();

    await channel.ExchangeDeclareAsync(
        exchange: "topic-exchange",
        type: ExchangeType.Topic,
        durable: true,
        autoDelete: false);

    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

    var props = new BasicProperties
    {
        ContentType = "application/json",
        DeliveryMode = DeliveryModes.Persistent
    };

    // Приклад routing key: "eu.orders.urgent" або "us.payments.failed"
    await channel.BasicPublishAsync(
        exchange: "topic-exchange",
        routingKey: request.TopicRoutingKey,
        mandatory: true,
        basicProperties: props,
        body: body);

    logger.LogInformation("🏷️ [Topic] Відправлено в 'topic-exchange' з ключем '{TopicKey}': {Data}",
        request.TopicRoutingKey, request.Data);

    return Results.Ok(new { Status = "Dispatched", Exchange = "topic-exchange", request.TopicRoutingKey, request.Data });
})
.WithName("SendTopicMessage")
.WithSummary("Відправити повідомлення у Topic Exchange (маршрутизація за паттерном регіон.тип.пріоритет)");

// =========================================================================
// 4. DEAD LETTER EXCHANGE (DLX): Повідомлення з TTL (Time-To-Live)
// =========================================================================
rabbitGroup.MapPost("/dead-letter-test", async (DeadLetterRequest request, IConnection connection, ILogger<Program> logger) =>
{
    await using var channel = await connection.CreateChannelAsync();

    // 1. Оголошуємо Dead-Letter Exchange
    await channel.ExchangeDeclareAsync("dlx-exchange", ExchangeType.Direct, durable: true);

    // 2. Оголошуємо основну чергу з аргументами перенаправлення (x-dead-letter-exchange)
    var queueArgs = new Dictionary<string, object?>
    {
        ["x-dead-letter-exchange"] = "dlx-exchange",
        ["x-dead-letter-routing-key"] = "dead-letter-routing-key"
    };

    await channel.QueueDeclareAsync(
        queue: "work-queue-with-dlx",
        durable: true,
        exclusive: false,
        autoDelete: false,
        arguments: queueArgs);

    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

    // Встановлюємо TTL (час життя) повідомлення у мілісекундах через заголовок Expiration
    var props = new BasicProperties
    {
        ContentType = "application/json",
        DeliveryMode = DeliveryModes.Persistent,
        Expiration = request.TtlMilliseconds.ToString() // Наприклад, 5000ms
    };

    await channel.BasicPublishAsync(
        exchange: string.Empty, // Default exchange напряму в чергу
        routingKey: "work-queue-with-dlx",
        mandatory: true,
        basicProperties: props,
        body: body);

    logger.LogWarning("⏳ [DLX Test] Відправлено повідомлення з TTL {Ttl}ms у 'work-queue-with-dlx'. Якщо його не оброблять вчасно, брокер автоматично перекине його в 'dead-letter-queue'!",
        request.TtlMilliseconds);

    return Results.Ok(new
    {
        Message = $"Повідомлення відправлено з TTL {request.TtlMilliseconds}ms. Спостерігайте за ним у RabbitMQ Management UI!",
        request.Payload
    });
})
.WithName("SendDeadLetterTest")
.WithSummary("Відправити повідомлення з обмеженим TTL для демонстрації Dead Letter Exchange (DLX)");

app.Run();

public record DirectMessageRequest(Guid MessageId, string RoutingKey, string Payload);
public record FanoutMessageRequest(string BroadcastText);
public record TopicMessageRequest(string TopicRoutingKey, string Data);
public record DeadLetterRequest(string Payload, int TtlMilliseconds = 5000);
