using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Orders.Api.Diagnostics;

/// <summary>
/// 📐 Центр конфігурації метрик та трасування OpenTelemetry для Orders.Api.
/// Використовує нативні абстракції .NET (System.Diagnostics.ActivitySource та System.Diagnostics.Metrics.Meter).
/// </summary>
public static class DiagnosticsConfig
{
    public const string ServiceName = "Orders.Api";
    public const string ActivitySourceName = "Mastery.DistributedShop";
    public const string MeterName = "Mastery.Shop.Metrics";

    // 1. Джерело активностей (Spans) для наскрізного трасування W3C
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

    // 2. Джерело вимірювань для метрик
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // Лічильник створених замовлень (монотонно зростає)
    public static readonly Counter<long> OrdersPlacedCounter = 
        Meter.CreateCounter<long>("shop.orders.placed", description: "Загальна кількість успішно створених замовлень");

    // Гістограма тривалості обробки замовлень у мілісекундах (дозволяє рахувати p50, p95, p99)
    public static readonly Histogram<double> OrderProcessingDuration = 
        Meter.CreateHistogram<double>("shop.order.duration.ms", unit: "ms", description: "Тривалість обробки замовлення у мс");

    // Показник активних сесій кошиків (може коливатися вгору і вниз)
    private static int _activeCarts = 12;
    public static readonly ObservableGauge<int> ActiveCartsGauge = 
        Meter.CreateObservableGauge<int>("shop.active_carts", () => _activeCarts, description: "Поточна кількість активних кошиків");

    public static void UpdateActiveCarts(int delta)
    {
        Interlocked.Add(ref _activeCarts, delta);
    }
}
