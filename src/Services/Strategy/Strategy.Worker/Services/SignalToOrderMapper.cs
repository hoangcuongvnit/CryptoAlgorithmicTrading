using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Session;
using Microsoft.Extensions.Options;
using Strategy.Worker.Configuration;

namespace Strategy.Worker.Services;

public sealed class SignalToOrderMapper
{
    private readonly TradingSettings _settings;

    public SignalToOrderMapper(IOptions<TradingSettings> settings)
    {
        _settings = settings.Value;
    }

    public bool TryMap(TradeSignal signal, out OrderRequest? orderRequest, SessionInfo? session = null)
    {
        orderRequest = null;

        // In SoftUnwind phase, only allow Strong signals
        if (session is not null && session.CurrentPhase == SessionPhase.SoftUnwind
            && signal.Strength != SignalStrength.Strong)
        {
            return false;
        }

        if ((int)signal.Strength < (int)_settings.MinimumSignalStrength)
        {
            return false;
        }

        var side = signal.Ema9 >= signal.Ema21 ? OrderSide.Buy : OrderSide.Sell;
        var entry = signal.BbMiddle > 0 ? signal.BbMiddle : signal.Ema9;

        if (entry <= 0)
        {
            return false;
        }

        decimal stopLoss, takeProfit;

        if (_settings.AdaptiveStopLossEnabled && signal.Atr14 > 0)
        {
            // Phase 2.1: SL = entry ± AtrMultiplier × ATR14; TP fixed at 2× risk
            var slDistance = _settings.AtrSlMultiplier * signal.Atr14 / entry;
            stopLoss = side == OrderSide.Buy
                ? entry * (1m - slDistance)
                : entry * (1m + slDistance);
            takeProfit = side == OrderSide.Buy
                ? entry + 2m * (entry - stopLoss)
                : entry - 2m * (stopLoss - entry);
        }
        else
        {
            // Fallback: fixed 1.5% SL / 3% TP
            stopLoss = side == OrderSide.Buy ? entry * 0.985m : entry * 1.015m;
            takeProfit = side == OrderSide.Buy ? entry * 1.03m : entry * 0.97m;
        }

        // Guard against extreme ATR producing non-positive SL or TP
        if (stopLoss <= 0 || takeProfit <= 0)
            return false;

        var quantity = ResolveOrderQuantity(entry);
        if (quantity <= 0)
        {
            return false;
        }

        orderRequest = new OrderRequest
        {
            Symbol = signal.Symbol,
            Side = side,
            Type = OrderType.Market,
            Quantity = quantity,
            Price = entry,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            StrategyName = "SignalStrengthEmaStrategy",
            SessionId = session?.SessionId,
            SessionPhase = session?.CurrentPhase,
            IsReduceOnly = false
        };

        return true;
    }

    private decimal ResolveOrderQuantity(decimal entryPrice)
    {
        var quantity = _settings.DefaultOrderQuantity;

        if (_settings.DefaultOrderNotionalUsdt > 0 && entryPrice > 0)
        {
            quantity = _settings.DefaultOrderNotionalUsdt / entryPrice;
        }

        if (_settings.MinOrderNotionalUsdt > 0 && entryPrice > 0)
        {
            var minQty = _settings.MinOrderNotionalUsdt / entryPrice;
            if (quantity < minQty)
            {
                quantity = minQty;
            }
        }

        return decimal.Round(quantity, 8, MidpointRounding.AwayFromZero);
    }
}
