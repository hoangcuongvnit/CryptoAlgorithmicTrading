using CryptoTrading.Shared.Constants;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Json;
using StackExchange.Redis;
using System.Text.Json;

namespace Analyzer.Worker.Infrastructure;

public sealed class SignalPublisher
{
    private readonly ISubscriber _subscriber;
    private readonly ILogger<SignalPublisher> _logger;

    public SignalPublisher(IConnectionMultiplexer redis, ILogger<SignalPublisher> logger)
    {
        _subscriber = redis.GetSubscriber();
        _logger = logger;
    }

    public async ValueTask PublishAsync(TradeSignal signal, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(signal, TradingJsonContext.Default.TradeSignal);
            await _subscriber.PublishAsync(
                RedisChannel.Literal(RedisChannels.Signal(signal.Symbol)),
                json);

            // Phase 3.1: Write regime to a Redis key so RiskGuard can apply position-size multipliers
            var db = _subscriber.Multiplexer.GetDatabase();
            await db.StringSetAsync(
                $"market:regime:{signal.Symbol}",
                signal.Regime.ToString(),
                TimeSpan.FromHours(2));   // expire after 2 h of no data

            _logger.LogDebug(
                "Published {Strength} signal for {Symbol}: RSI={Rsi:F1} EMA9={Ema9:F2} EMA21={Ema21:F2} Regime={Regime}",
                signal.Strength, signal.Symbol, signal.Rsi, signal.Ema9, signal.Ema21, signal.Regime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish signal for {Symbol}", signal.Symbol);
        }
    }
}
