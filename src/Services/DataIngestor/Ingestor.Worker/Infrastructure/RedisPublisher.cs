using StackExchange.Redis;
using System.Text.Json;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Json;
using CryptoTrading.Shared.Constants;

namespace Ingestor.Worker.Infrastructure;

public sealed class RedisPublisher
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ISubscriber _subscriber;
    private readonly ILogger<RedisPublisher> _logger;

    public RedisPublisher(
        IConnectionMultiplexer redis,
        ILogger<RedisPublisher> logger)
    {
        _redis = redis;
        _subscriber = redis.GetSubscriber();
        _logger = logger;
    }

    public async ValueTask PublishPriceTickAsync(PriceTick tick, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(tick, TradingJsonContext.Default.PriceTick);
            var channel = RedisChannels.Price(tick.Symbol);
            
            await _subscriber.PublishAsync(
                RedisChannel.Literal(channel),
                json);

            _logger.LogTrace("Published price tick for {Symbol}: ${Price}", tick.Symbol, tick.Price);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish price tick for {Symbol}", tick.Symbol);
        }
    }

    public async ValueTask PublishSystemEventAsync(SystemEvent evt, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(evt, TradingJsonContext.Default.SystemEvent);
            
            await _subscriber.PublishAsync(
                RedisChannel.Literal(RedisChannels.SystemEvents),
                json);

            _logger.LogDebug("Published system event: {Type} from {Service}", evt.Type, evt.ServiceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish system event");
        }
    }

    public async Task<bool> IsConnectedAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.PingAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
