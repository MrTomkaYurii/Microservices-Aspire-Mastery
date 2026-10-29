using System.Diagnostics;
using Observability.Aspire.Demo.Diagnostics;
using Observability.Aspire.Demo.Health;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, HealthChecks, ServiceDiscovery, Resiliency)
builder.AddServiceDefaults();

// Register custom OpenTelemetry ActivitySource and Meter
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(DiagnosticsConfig.ActivitySourceName);
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(DiagnosticsConfig.MeterName);
    });

// Register custom HealthCheck for downstream service
builder.Services.AddHealthChecks()
    .AddCheck<PaymentGatewayHealthCheck>("payment_gateway", tags: new[] { "live", "ready" });

var app = builder.Build();

app.MapDefaultEndpoints(); // Maps /health and /alive endpoints from Aspire ServiceDefaults

app.MapGet("/", () => Results.Ok(new
{
    Service = "Observability.Aspire.Demo (.NET 10 & OpenTelemetry)",
    Endpoints = new[] { "/api/order", "/health", "/alive" }
}));

app.MapPost("/api/order", async (OrderSimulationRequest request) =>
{
    var stopwatch = Stopwatch.StartNew();

    // Start custom distributed tracing Activity (span)
    using var activity = DiagnosticsConfig.ActivitySource.StartActivity("ProcessDistributedOrder");

    activity?.SetTag("shop.order.id", request.OrderId);
    activity?.SetTag("shop.customer.id", request.CustomerId);
    activity?.SetTag("shop.order.amount", request.Amount);

    // Simulate work across distributed components
    await Task.Delay(Random.Shared.Next(30, 100));

    // Record metrics
    DiagnosticsConfig.OrdersPlacedCounter.Add(1, 
        new KeyValuePair<string, object?>("currency", request.Currency));

    stopwatch.Stop();
    DiagnosticsConfig.OrderProcessingDuration.Record(stopwatch.ElapsedMilliseconds);

    activity?.AddEvent(new ActivityEvent("OrderProcessingCompleted"));

    return Results.Ok(new
    {
        Status = "Completed",
        request.OrderId,
        TraceId = Activity.Current?.TraceId.ToString(),
        SpanId = Activity.Current?.SpanId.ToString(),
        DurationMs = stopwatch.ElapsedMilliseconds
    });
});

app.Run();

public record OrderSimulationRequest(string OrderId, string CustomerId, decimal Amount, string Currency = "USD");
