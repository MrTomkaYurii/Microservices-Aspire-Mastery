using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Observability.Aspire.Demo.Diagnostics;

public static class DiagnosticsConfig
{
    public const string ServiceName = "Observability.Aspire.Demo";
    public const string ActivitySourceName = "Mastery.DistributedShop";
    public const string MeterName = "Mastery.Shop.Metrics";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> OrdersPlacedCounter = 
        Meter.CreateCounter<long>("shop.orders.placed", description: "Total count of successfully placed orders");

    public static readonly Histogram<double> OrderProcessingDuration = 
        Meter.CreateHistogram<double>("shop.order.duration.ms", unit: "ms", description: "Duration of order processing in milliseconds");
}
