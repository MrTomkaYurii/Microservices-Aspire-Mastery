using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Orders.Api.Diagnostics;

namespace Orders.Api.Controllers;

/// <summary>
/// 📊 КРОК 3: ЧИСЛОВІ ПОКАЗНИКИ ТА МЕТРИКИ (METRICS: COUNTER, HISTOGRAM, GAUGE)
/// 
/// 💡 РІЗНИЦЯ МІЖ ЛОГАМИ, ТРАСУВАННЯМ ТА МЕТРИКАМИ:
/// - Логи (Logs): Детальні текстові події (що сталося о 12:00:01 у деталях).
/// - Траси (Traces): Шлях одного конкретного запиту через усі мікросервіси.
/// - Метрики (Metrics): Агреговані числа у часі (скільки запитів за секунду, скільки пам'яті, 95-й перцентиль затримки).
/// 
/// 📐 ТРИ ОСНОВНІ ТИПИ МЕТРИК У .NET:
/// 1. Counter (Лічильник): Число, що тільки зростає (кількість створених замовлень, помилок).
/// 2. Histogram (Гістограма): Розподіл значень за інтервалами (час відповіді у мс для розрахунку p50, p95, p99).
/// 3. Gauge (Датчик): Значення, що піднімається та опускається (активні з'єднання, зайнятість черги).
/// </summary>
[ApiController]
[Route("api/step3-metrics")]
public class Step3_MetricsController : ControllerBase
{
    /// <summary>
    /// Зафіксувати подію створення замовлення та оновити відповідні метрики.
    /// </summary>
    [HttpPost("record-order")]
    public async Task<IActionResult> RecordOrderMetrics([FromBody] RecordOrderMetricsRequest request)
    {
        var stopwatch = Stopwatch.StartNew();

        // Симуляція обробки замовлення
        var simulatedWorkMs = Random.Shared.Next(25, 120);
        await Task.Delay(simulatedWorkMs);

        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;

        // 1. Збільшуємо лічильник замовлень (Counter) з вимірами (Dimensions/Tags)
        DiagnosticsConfig.OrdersPlacedCounter.Add(1, 
            new KeyValuePair<string, object?>("currency", request.Currency),
            new KeyValuePair<string, object?>("tier", request.Tier));

        // 2. Фіксуємо тривалість у гістограмі (Histogram) для підрахунку перцентилів
        DiagnosticsConfig.OrderProcessingDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("tier", request.Tier));

        return Ok(new
        {
            Preamble = "Метрики зафіксовано через .NET System.Diagnostics.Metrics.Meter.",
            request.OrderId,
            MetricsUpdated = new
            {
                Counter = "shop.orders.placed (+1)",
                Histogram = $"shop.order.duration.ms ({elapsedMs} ms)",
                Dimensions = new { request.Currency, request.Tier }
            },
            AnalyticValue = "Метрики автоматично експортуються через OTLP в Prometheus / Aspire Dashboard для побудови графіків RPS та Latency."
        });
    }

    /// <summary>
    /// Змінити значення датчика активних кошиків (Gauge), демонструючи коливання показників.
    /// </summary>
    [HttpPost("adjust-cart-gauge")]
    public IActionResult AdjustCartGauge([FromQuery] int delta)
    {
        DiagnosticsConfig.UpdateActiveCarts(delta);

        return Ok(new
        {
            Message = $"Показник 'shop.active_carts' змінено на {delta:+0;-0;0}.",
            GaugeType = "ObservableGauge<int>",
            Note = "Значення датчика періодично опитується OpenTelemetry runtime без додаткових накладних витрат."
        });
    }
}

public record RecordOrderMetricsRequest(string OrderId, string Currency = "USD", string Tier = "Standard");
