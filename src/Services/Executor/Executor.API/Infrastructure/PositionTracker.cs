using CryptoTrading.Shared.DTOs;
using System.Collections.Concurrent;

namespace Executor.API.Infrastructure;

public sealed class PositionTracker
{
    private readonly ConcurrentDictionary<string, OpenPosition> _positions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ClosedTrade> _closedTrades = [];
    private readonly Lock _closeLock = new();

    public sealed record OpenPosition(
        string Symbol,
        decimal Quantity,
        decimal AvgEntryPrice,
        decimal CurrentPrice,
        DateTime OpenedAt);

    public sealed record ClosedTrade(
        string Symbol,
        decimal Quantity,
        decimal EntryPrice,
        decimal ExitPrice,
        decimal RealizedPnL,
        DateTime ClosedAt);

    public sealed record TradingStats(
        int TotalTrades,
        int WinTrades,
        int LossTrades,
        decimal TotalPnL,
        decimal WinRate,
        decimal MaxDrawdown,
        decimal AvgPnLPerTrade);

    public void OnOrderFilled(OrderRequest request, OrderResult result)
    {
        if (!result.Success || result.FilledQty <= 0) return;

        if (request.Side == OrderSide.Buy)
        {
            _positions.AddOrUpdate(
                result.Symbol,
                _ => new OpenPosition(result.Symbol, result.FilledQty, result.FilledPrice, result.FilledPrice, result.Timestamp),
                (_, existing) =>
                {
                    var totalQty = existing.Quantity + result.FilledQty;
                    var avgPrice = (existing.AvgEntryPrice * existing.Quantity + result.FilledPrice * result.FilledQty) / totalQty;
                    return existing with { Quantity = totalQty, AvgEntryPrice = avgPrice, CurrentPrice = result.FilledPrice };
                });
        }
        else if (request.Side == OrderSide.Sell)
        {
            if (!_positions.TryGetValue(result.Symbol, out var pos)) return;

            var closedQty = Math.Min(result.FilledQty, pos.Quantity);
            // Binance fee: 0.1% per side
            var fees = closedQty * (result.FilledPrice + pos.AvgEntryPrice) * 0.001m;
            var pnl = (result.FilledPrice - pos.AvgEntryPrice) * closedQty - fees;

            lock (_closeLock)
            {
                _closedTrades.Add(new ClosedTrade(result.Symbol, closedQty, pos.AvgEntryPrice, result.FilledPrice, pnl, result.Timestamp));
            }

            var remaining = pos.Quantity - closedQty;
            if (remaining <= 0)
                _positions.TryRemove(result.Symbol, out _);
            else
                _positions[result.Symbol] = pos with { Quantity = remaining };
        }
    }

    public void UpdateCurrentPrice(string symbol, decimal price)
    {
        if (_positions.TryGetValue(symbol, out var pos))
            _positions[symbol] = pos with { CurrentPrice = price };
    }

    public IReadOnlyList<object> GetOpenPositions()
    {
        return _positions.Values.Select(p =>
        {
            var unrealizedPnL = (p.CurrentPrice - p.AvgEntryPrice) * p.Quantity;
            var roe = p.AvgEntryPrice > 0
                ? unrealizedPnL / (p.AvgEntryPrice * p.Quantity) * 100m
                : 0m;
            return (object)new
            {
                symbol = p.Symbol,
                side = "Buy",
                quantity = p.Quantity,
                entryPrice = p.AvgEntryPrice,
                currentPrice = p.CurrentPrice,
                unrealizedPnL = decimal.Round(unrealizedPnL, 4),
                roe = decimal.Round(roe, 2),
                openedAt = p.OpenedAt
            };
        }).ToList();
    }

    public TradingStats GetStats()
    {
        List<ClosedTrade> trades;
        lock (_closeLock) trades = [.. _closedTrades];

        if (trades.Count == 0)
            return new TradingStats(0, 0, 0, 0m, 0m, 0m, 0m);

        var totalPnL = trades.Sum(t => t.RealizedPnL);
        var wins = trades.Count(t => t.RealizedPnL > 0);
        var maxDrawdown = CalculateMaxDrawdown(trades);

        return new TradingStats(
            TotalTrades: trades.Count,
            WinTrades: wins,
            LossTrades: trades.Count - wins,
            TotalPnL: decimal.Round(totalPnL, 4),
            WinRate: decimal.Round((decimal)wins / trades.Count, 4),
            MaxDrawdown: decimal.Round(maxDrawdown, 4),
            AvgPnLPerTrade: decimal.Round(totalPnL / trades.Count, 4));
    }

    private static decimal CalculateMaxDrawdown(List<ClosedTrade> trades)
    {
        decimal peak = 0m, maxDrawdown = 0m, running = 0m;
        foreach (var t in trades.OrderBy(x => x.ClosedAt))
        {
            running += t.RealizedPnL;
            if (running > peak) peak = running;
            if (peak > 0)
            {
                var dd = (peak - running) / peak;
                if (dd > maxDrawdown) maxDrawdown = dd;
            }
        }
        return maxDrawdown;
    }
}
