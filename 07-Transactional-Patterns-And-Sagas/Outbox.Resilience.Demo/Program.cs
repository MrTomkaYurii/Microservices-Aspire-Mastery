using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Outbox.Resilience.Demo.BackgroundServices;
using Outbox.Resilience.Demo.Data;
using Outbox.Resilience.Demo.Entities;

var builder = WebApplication.CreateBuilder(args);

// Register EF Core InMemory Database (or connect to Postgres/SQL Server)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseInMemoryDatabase("OutboxDemoDb");
});

// Register Outbox Publisher Background Worker with Polly v8
builder.Services.AddHostedService<OutboxPublisherWorker>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Message = "Transactional Outbox & Resilience Demo (.NET 10 & Polly v8)",
    Endpoints = new[] { "POST /api/orders", "GET /api/orders", "GET /api/outbox" }
}));

// Atomic Transactional Outbox pattern: write Business Entity and Outbox Message in single transaction
app.MapPost("/api/orders", async (CreateOrderRequest request, AppDbContext dbContext) =>
{
    var order = new Order
    {
        CustomerId = request.CustomerId,
        TotalAmount = request.TotalAmount,
        Status = "Pending"
    };

    var orderCreatedEvent = new
    {
        OrderId = order.Id,
        order.CustomerId,
        order.TotalAmount,
        CreatedAt = DateTime.UtcNow
    };

    var outboxMessage = new OutboxMessage
    {
        EventType = "OrderCreatedIntegrationEvent",
        Payload = JsonSerializer.Serialize(orderCreatedEvent),
        CreatedAt = DateTime.UtcNow
    };

    // Both saved atomically
    dbContext.Orders.Add(order);
    dbContext.OutboxMessages.Add(outboxMessage);
    await dbContext.SaveChangesAsync();

    return Results.Accepted($"/api/orders/{order.Id}", new
    {
        order.Id,
        order.Status,
        OutboxMessageId = outboxMessage.Id,
        Message = "Order created and queued to Outbox for guaranteed at-least-once delivery."
    });
});

app.MapGet("/api/orders", async (AppDbContext dbContext) =>
{
    var orders = await dbContext.Orders.ToListAsync();
    return Results.Ok(orders);
});

app.MapGet("/api/outbox", async (AppDbContext dbContext) =>
{
    var messages = await dbContext.OutboxMessages.OrderByDescending(m => m.CreatedAt).Take(50).ToListAsync();
    return Results.Ok(messages);
});

app.Run();

public record CreateOrderRequest(string CustomerId, decimal TotalAmount);
