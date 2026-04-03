using CryptoTrading.Shared.Constants;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Json;
using StackExchange.Redis;
using System.Text.Json;

namespace Executor.API.Infrastructure;

public sealed class SystemEventPublisher
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SystemEventPublisher> _logger;

    public SystemEventPublisher(IConnectionMultiplexer redis, ILogger<SystemEventPublisher> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Publishes a system event to the system:events Redis channel.
    /// Swallows exceptions after logging — monitoring must never block trading.
    /// </summary>
    public async Task PublishAsync(SystemEvent evt, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(evt, TradingJsonContext.Default.SystemEvent);
            var sub = _redis.GetSubscriber();
            await sub.PublishAsync(RedisChannel.Literal(RedisChannels.SystemEvents), json, CommandFlags.FireAndForget);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish system event {Type} from Executor", evt.Type);
        }
    }
}
