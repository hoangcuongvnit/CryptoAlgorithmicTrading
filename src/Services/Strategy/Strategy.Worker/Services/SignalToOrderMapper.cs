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

        var stopLoss = side == OrderSide.Buy
            ? entry * 0.985m
            : entry * 1.015m;

        var takeProfit = side == OrderSide.Buy
            ? entry * 1.03m
            : entry * 0.97m;

        orderRequest = new OrderRequest
        {
            Symbol = signal.Symbol,
            Side = side,
            Type = OrderType.Market,
            Quantity = _settings.DefaultOrderQuantity,
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
}
