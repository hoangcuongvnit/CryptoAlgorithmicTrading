using System.Diagnostics.Metrics;

namespace Executor.API.Infrastructure;

public sealed class ReconciliationMetrics : IDisposable
{
    private readonly Meter _meter = new("Executor.API.Reconciliation", "1.0.0");
    private readonly Counter<long> _cyclesTotal;
    private readonly Counter<long> _driftsDetected;
    private readonly Counter<long> _driftsRecovered;
    private readonly Histogram<double> _cycleDurationMs;
    private long _lastSuccessfulCycleUnix;
    private long _consecutiveFailures;

    public ReconciliationMetrics()
    {
        _cyclesTotal = _meter.CreateCounter<long>("reconciliation.cycles.total");
        _driftsDetected = _meter.CreateCounter<long>("reconciliation.drifts.detected.total");
        _driftsRecovered = _meter.CreateCounter<long>("reconciliation.drifts.recovered.total");
        _cycleDurationMs = _meter.CreateHistogram<double>("reconciliation.cycle.duration_ms");

        _meter.CreateObservableGauge(
            "reconciliation.last_successful_cycle_unix",
            () => new Measurement<long>[] { new(_lastSuccessfulCycleUnix) });

        _meter.CreateObservableGauge(
            "reconciliation.consecutive_failures",
            () => new Measurement<long>[] { new(_consecutiveFailures) });
    }

    public void RecordCycleStart() => _cyclesTotal.Add(1);

    public void RecordDrifts(int count)
    {
        if (count > 0)
            _driftsDetected.Add(count);
    }

    public void RecordRecovered(int count)
    {
        if (count > 0)
            _driftsRecovered.Add(count);
    }

    public void RecordCycleComplete(TimeSpan duration, bool success)
    {
        _cycleDurationMs.Record(duration.TotalMilliseconds);
        if (success)
        {
            _lastSuccessfulCycleUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _consecutiveFailures = 0;
        }
        else
        {
            _consecutiveFailures++;
        }
    }

    public object Snapshot() => new
    {
        lastSuccessfulCycleUtc = _lastSuccessfulCycleUnix > 0
            ? (DateTime?)DateTimeOffset.FromUnixTimeSeconds(_lastSuccessfulCycleUnix).UtcDateTime
            : null,
        consecutiveFailures = _consecutiveFailures
    };

    public Meter GetMeter() => _meter;

    public void Dispose() => _meter.Dispose();
}
