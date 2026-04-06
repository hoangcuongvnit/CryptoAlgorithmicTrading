using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Session;
using Microsoft.Extensions.Options;
using Strategy.Worker.Configuration;
using Strategy.Worker.Infrastructure;

namespace Strategy.Worker.Services;

public sealed class SignalToOrderMapper
{
    private readonly TradingSettings _settings;
    private readonly StrategyConfigStore _configStore;

    public SignalToOrderMapper(IOptions<TradingSettings> settings, StrategyConfigStore configStore)
    {
        _settings = settings.Value;
        _configStore = configStore;
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

        var defaultNotional = _configStore.DefaultOrderNotionalUsdt;
        if (defaultNotional > 0 && entryPrice > 0)
            quantity = defaultNotional / entryPrice;

        var minNotional = _configStore.MinOrderNotionalUsdt;
        if (minNotional > 0 && entryPrice > 0)
        {
            var minQty = minNotional / entryPrice;
            if (quantity < minQty)
                quantity = minQty;
        }

        return decimal.Round(quantity, 8, MidpointRounding.AwayFromZero);
    }
}
