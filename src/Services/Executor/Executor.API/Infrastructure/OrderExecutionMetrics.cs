using System.Diagnostics.Metrics;

namespace Executor.API.Infrastructure;

/// <summary>
/// Custom metrics for order execution monitoring with OpenTelemetry.
/// Exposes Prometheus-compatible metrics via OTEL.
/// </summary>
public sealed class OrderExecutionMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _ordersPlacedCounter;
    private readonly Counter<long> _ordersRejectedCounter;
    private readonly Counter<long> _ordersFilledCounter;
    private readonly Histogram<double> _orderLatencyMs;
    private readonly Histogram<double> _fillPriceDeviation;
    private readonly UpDownCounter<long> _activeOrdersGauge;
    private readonly Counter<long> _auditEventsPublishedCounter;

    public OrderExecutionMetrics()
    {
        _meter = new Meter("Executor.API.Metrics", "1.0.0");

        // Counters
        _ordersPlacedCounter = _meter.CreateCounter<long>(
            "executor.orders.placed.total",
            description: "Total number of orders placed (paper + live)");

        _ordersRejectedCounter = _meter.CreateCounter<long>(
            "executor.orders.rejected.total",
            description: "Total number of orders rejected by validation");

        _ordersFilledCounter = _meter.CreateCounter<long>(
            "executor.orders.filled.total",
            description: "Total number of orders successfully filled");

        _auditEventsPublishedCounter = _meter.CreateCounter<long>(
            "executor.audit.events.published.total",
            description: "Total number of audit events published to Redis Streams");

        // Histograms
        _orderLatencyMs = _meter.CreateHistogram<double>(
            "executor.order.latency.ms",
            unit: "ms",
            description: "Order execution latency in milliseconds");

        _fillPriceDeviation = _meter.CreateHistogram<double>(
            "executor.fill.price.deviation.percent",
            unit: "%",
            description: "Deviation of fill price from reference price (slippage)");

        // Gauge (up-down counter)
        _activeOrdersGauge = _meter.CreateUpDownCounter<long>(
            "executor.orders.active",
            description: "Current number of active orders being processed");
    }

    public void RecordOrderPlaced(string symbol, string side, decimal quantity)
    {
        _ordersPlacedCounter.Add(1, new KeyValuePair<string, object?>("symbol", symbol), new KeyValuePair<string, object?>("side", side));
    }

    public void RecordOrderRejected(string symbol, string reason)
    {
        _ordersRejectedCounter.Add(1, new KeyValuePair<string, object?>("symbol", symbol), new KeyValuePair<string, object?>("reason", reason));
    }

    public void RecordOrderFilled(string symbol, decimal quantity, decimal fillPrice, bool isPaper)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("symbol", symbol),
            new KeyValuePair<string, object?>("is_paper", isPaper)
        };
        _ordersFilledCounter.Add(1, tags);
    }

    public void RecordOrderLatency(double latencyMilliseconds, string symbol)
    {
        _orderLatencyMs.Record(latencyMilliseconds, new KeyValuePair<string, object?>("symbol", symbol));
    }

    public void RecordFillPriceDeviation(double deviationPercent, string symbol)
    {
        _fillPriceDeviation.Record(deviationPercent, new KeyValuePair<string, object?>("symbol", symbol));
    }

    public void IncrementActiveOrders(int delta = 1)
    {
        _activeOrdersGauge.Add(delta);
    }

    public void RecordAuditEventPublished()
    {
        _auditEventsPublishedCounter.Add(1);
    }

    public Meter GetMeter() => _meter;

    public void Dispose()
    {
        _meter?.Dispose();
    }
}
