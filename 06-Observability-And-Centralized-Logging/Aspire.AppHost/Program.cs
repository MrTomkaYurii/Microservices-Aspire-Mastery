var builder = DistributedApplication.CreateBuilder(args);

// =========================================================================================
// 🎯 МОДУЛЬ 06: ОРКЕСТРАЦІЯ СПОСТЕРЕЖУВАНОСТІ (OBSERVABILITY & CENTRALIZED LOGGING)
// =========================================================================================
// 1. Seq Log Server:
//    Піднімаємо централізований сервер збору логів Seq через офіційний ресурс Aspire.
//    Seq дозволяє шукати структуровані події у реальному часі за полями (наприклад, @OrderId == '123').
var seq = builder.AddSeq("seq");

// 2. Orders.Api:
//    Основний сервіс замовлень із підтримкою 4 покрокових контролерів:
//    - Health Checks (Liveness vs Readiness)
//    - Distributed Tracing (W3C TraceContext)
//    - Metrics (Meters, Counters, Histograms)
//    - Structured Logging (Serilog sink to Seq)
builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithReference(seq);

builder.Build().Run();
