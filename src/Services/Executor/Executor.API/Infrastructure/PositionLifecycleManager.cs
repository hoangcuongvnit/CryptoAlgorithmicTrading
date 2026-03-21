using CryptoTrading.Shared.Session;

namespace Executor.API.Infrastructure;

/// <summary>
/// Session-scoped wrapper around PositionTracker providing
/// exposure status and priority-sorted close queues.
/// </summary>
public sealed class PositionLifecycleManager
{
    private readonly PositionTracker _positionTracker;
    private readonly SessionClock _sessionClock;
    private string? _currentSessionId;

    public PositionLifecycleManager(PositionTracker positionTracker, SessionClock sessionClock)
    {
        _positionTracker = positionTracker;
        _sessionClock = sessionClock;
    }

    public sealed record PositionCloseCandidate(
        string Symbol,
        decimal Quantity,
        decimal UnrealizedPnL,
        decimal Notional,
        int Priority);

    public string CurrentSessionId
    {
        get
        {
            _currentSessionId ??= _sessionClock.GetCurrentSession().SessionId;
            return _currentSessionId;
        }
    }

    public bool IsFlat()
    {
        var positions = _positionTracker.GetOpenPositions();
        return positions.Count == 0;
    }

    public decimal GetSessionExposure()
    {
        var positions = _positionTracker.GetOpenPositions();
        return positions.Sum(p =>
        {
            var qty = (decimal)(p.GetType().GetProperty("quantity")?.GetValue(p) ?? 0m);
            var price = (decimal)(p.GetType().GetProperty("currentPrice")?.GetValue(p) ?? 0m);
            return qty * price;
        });
    }

    public IReadOnlyList<PositionCloseCandidate> GetCloseQueue()
    {
        var positions = _positionTracker.GetOpenPositions();
        var candidates = new List<PositionCloseCandidate>();

        foreach (var pos in positions)
        {
            var symbol = pos.GetType().GetProperty("symbol")?.GetValue(pos)?.ToString() ?? "";
            var quantity = (decimal)(pos.GetType().GetProperty("quantity")?.GetValue(pos) ?? 0m);
            var entryPrice = (decimal)(pos.GetType().GetProperty("entryPrice")?.GetValue(pos) ?? 0m);
            var currentPrice = (decimal)(pos.GetType().GetProperty("currentPrice")?.GetValue(pos) ?? 0m);
            var unrealizedPnL = (decimal)(pos.GetType().GetProperty("unrealizedPnL")?.GetValue(pos) ?? 0m);
            var notional = quantity * currentPrice;

            candidates.Add(new PositionCloseCandidate(symbol, quantity, unrealizedPnL, notional, 0));
        }

        // Priority: highest loss first, then largest notional
        return candidates
            .OrderBy(c => c.UnrealizedPnL)
            .ThenByDescending(c => c.Notional)
            .Select((c, i) => c with { Priority = i + 1 })
            .ToList();
    }

    public void OnSessionStart(string sessionId)
    {
        _currentSessionId = sessionId;
    }

    public void OnSessionEnd(string sessionId)
    {
        _currentSessionId = null;
    }
}
