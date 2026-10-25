using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Orders.Api.Diagnostics;

namespace Orders.Api.Controllers;

/// <summary>
/// 🛰️ КРОК 2: НАСКРІЗНЕ РОЗПОДІЛЕНЕ ТРАСУВАННЯ (DISTRIBUTED TRACING & W3C TRACECONTEXT)
/// 
/// 💡 АНАЛОГІЯ З ПОШТОВИМ ТРЕК-НОМЕРОМ:
/// Коли ви замовляєте посилку з Китаю в Україну, вона проходить літак, митницю, сортувальний центр і кур'єра.
/// На кожному етапі створюється внутрішня квитанція (Span), але ВСІ вони об'єднані єдиним 
/// міжнародним трек-номером (TraceId).
/// 
/// Стандарт W3C TraceContext:
/// Мікросервіси передають заголовок 'traceparent':
///   00 - 4bf92f3577b34da6a3ce929d0e0e4736 - 00f067aa0ba902b7 - 01
///   [версія] - [єдиний на всю систему TraceId] - [батьківський SpanId] - [прапорці запису]
/// </summary>
[ApiController]
[Route("api/step2-tracing")]
public class Step2_DistributedTracingController : ControllerBase
{
    private readonly ILogger<Step2_DistributedTracingController> _logger;

    public Step2_DistributedTracingController(ILogger<Step2_DistributedTracingController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Запустити розподілене замовлення та згенерувати ієрархію Spans (Parent -> DB Span -> External Gateway Span).
    /// </summary>
    [HttpPost("start-trace")]
    public async Task<IActionResult> StartDistributedTrace([FromBody] PlaceOrderTraceRequest request)
    {
        // 1. Початок кореневої активності (Root Span) нашого доменного джерела
        using var rootActivity = DiagnosticsConfig.ActivitySource.StartActivity("PlaceCustomerOrder", ActivityKind.Server);

        // Встановлюємо доменні теги (Attributes)
        rootActivity?.SetTag("order.id", request.OrderId);
        rootActivity?.SetTag("customer.id", request.CustomerId);
        rootActivity?.SetTag("order.amount", request.Amount);

        // Baggage - контекстні дані, що автоматично прокидаються в усі наступні HTTP-запити
        rootActivity?.SetBaggage("user.tier", "PremiumCustomer");

        var currentTraceId = Activity.Current?.TraceId.ToString() ?? "N/A";
        var rootSpanId = rootActivity?.SpanId.ToString() ?? "N/A";

        _logger.LogInformation("🚀 [TraceId={TraceId}] Початок обробки замовлення {OrderId}", currentTraceId, request.OrderId);

        // 2. Симуляція вкладеного кроку 1: Запис у базу даних (Child Span)
        string dbSpanId;
        using (var dbSpan = DiagnosticsConfig.ActivitySource.StartActivity("QueryDatabase.SaveOrder", ActivityKind.Internal))
        {
            dbSpan?.SetTag("db.system", "postgresql");
            dbSpan?.SetTag("db.statement", "INSERT INTO orders (id, amount) VALUES (@p0, @p1)");
            await Task.Delay(Random.Shared.Next(20, 50));
            dbSpanId = dbSpan?.SpanId.ToString() ?? "N/A";
        }

        // 3. Симуляція вкладеного кроку 2: Виклик платіжного сервісу (Child Span з викликом по мережі)
        string paymentSpanId;
        using (var paymentSpan = DiagnosticsConfig.ActivitySource.StartActivity("ExternalCall.StripeCharge", ActivityKind.Client))
        {
            paymentSpan?.SetTag("rpc.system", "http");
            paymentSpan?.SetTag("http.url", "https://api.stripe.com/v1/charges");
            paymentSpan?.AddEvent(new ActivityEvent("StripeTokenReceived"));
            await Task.Delay(Random.Shared.Next(40, 80));
            paymentSpanId = paymentSpan?.SpanId.ToString() ?? "N/A";
        }

        rootActivity?.AddEvent(new ActivityEvent("OrderSuccessfullyPersisted"));

        return Ok(new
        {
            Preamble = "Наскрізний ланцюжок трасування W3C сформовано та надіслано в OTLP/Aspire Dashboard.",
            request.OrderId,
            W3CTraceContext = new
            {
                TraceId = currentTraceId,
                RootSpanId = rootSpanId,
                ChildSpans = new[]
                {
                    new { Name = "QueryDatabase.SaveOrder", SpanId = dbSpanId, ParentSpanId = rootSpanId },
                    new { Name = "ExternalCall.StripeCharge", SpanId = paymentSpanId, ParentSpanId = rootSpanId }
                },
                TraceParentHeader = $"00-{currentTraceId}-{rootSpanId}-01"
            },
            Explanation = "Усі три операції мають ОДНАКОВИЙ TraceId, тому в Aspire Dashboard ви бачите дерево виконання з точним таймінгом кожного мікрокроку."
        });
    }
}

public record PlaceOrderTraceRequest(string OrderId, string CustomerId, decimal Amount);
