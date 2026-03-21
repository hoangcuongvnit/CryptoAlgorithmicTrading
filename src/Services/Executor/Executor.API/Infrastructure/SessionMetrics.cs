using System.Diagnostics.Metrics;

namespace Executor.API.Infrastructure;

/// <summary>
/// OpenTelemetry metrics for 4-hour session trading compliance.
/// </summary>
public sealed class SessionMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _openOrdersBlockedCounter;
    private readonly Counter<long> _forcedLiquidationsCounter;
    private readonly Counter<long> _sessionTransitionsCounter;
    private int _sessionOpenPositions;
    private double _minutesToSessionEnd;
    private int _sessionsTotal;
    private int _sessionsFlatTotal;

    public SessionMetrics()
    {
        _meter = new Meter("Executor.API.SessionMetrics", "1.0.0");

        _openOrdersBlockedCounter = _meter.CreateCounter<long>(
            "session.open_orders_blocked.total",
            description: "Orders blocked due to session liquidation window");

        _forcedLiquidationsCounter = _meter.CreateCounter<long>(
            "session.forced_liquidations.total",
            description: "Positions force-closed by liquidation orchestrator");

        _sessionTransitionsCounter = _meter.CreateCounter<long>(
            "session.transitions.total",
            description: "Total number of session transitions");

        _meter.CreateObservableGauge(
            "session.open_positions",
            () => _sessionOpenPositions,
            description: "Current number of open positions in session");

        _meter.CreateObservableGauge(
            "session.minutes_to_end",
            () => _minutesToSessionEnd,
            unit: "min",
            description: "Minutes remaining in current session");

        _meter.CreateObservableGauge(
            "session.flat_compliance_ratio",
            () => _sessionsTotal > 0 ? (double)_sessionsFlatTotal / _sessionsTotal : 1.0,
            description: "Ratio of sessions that ended flat (target: 1.0)");
    }

    public void UpdateSessionGauges(int openPositions, double minutesToEnd)
    {
        _sessionOpenPositions = openPositions;
        _minutesToSessionEnd = minutesToEnd;
    }

    public void RecordOrderBlocked(string reason)
    {
        _openOrdersBlockedCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }

    public void RecordForcedLiquidation(string symbol)
    {
        _forcedLiquidationsCounter.Add(1, new KeyValuePair<string, object?>("symbol", symbol));
    }

    public void RecordSessionTransition(string sessionId)
    {
        _sessionTransitionsCounter.Add(1, new KeyValuePair<string, object?>("session_id", sessionId));
    }

    public void RecordSessionEnd(bool wasFlat)
    {
        _sessionsTotal++;
        if (wasFlat) _sessionsFlatTotal++;
    }

    public Meter GetMeter() => _meter;

    public void Dispose()
    {
        _meter?.Dispose();
    }
}
