using CryptoTrading.Shared.Constants;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Json;
using StackExchange.Redis;
using System.Text.Json;

namespace RiskGuard.API.Infrastructure;

/// <summary>
/// Publishes <see cref="SystemEvent"/> messages to the Redis <c>system:events</c> Pub/Sub channel.
/// Failures are logged and swallowed — monitoring must never block trading.
/// </summary>
public sealed class SystemEventPublisher
{
    private readonly ISubscriber _subscriber;
    private readonly ILogger<SystemEventPublisher> _logger;

    public SystemEventPublisher(IConnectionMultiplexer redis, ILogger<SystemEventPublisher> logger)
    {
        _subscriber = redis.GetSubscriber();
        _logger = logger;
    }

    public async Task PublishAsync(SystemEvent evt, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(evt, TradingJsonContext.Default.SystemEvent);
            await _subscriber.PublishAsync(RedisChannel.Literal(RedisChannels.SystemEvents), json);
            _logger.LogDebug("Published system event: {Type} from {Service}", evt.Type, evt.ServiceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish system event: {Type}", evt.Type);
        }
    }
}
