using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace CryptoTrading.Shared.Timeline;

public sealed class RedisTimelineEventPublisher : ITimelineEventPublisher
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisTimelineEventPublisher> _logger;

    public RedisTimelineEventPublisher(
        IConnectionMultiplexer redis,
        ILogger<RedisTimelineEventPublisher> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task PublishAsync(TimelineEvent evt, CancellationToken ct = default)
    {
        try
        {
            var channel = $"coin:{evt.Symbol}:log";
            var json = JsonSerializer.Serialize(evt);
            var pub = _redis.GetSubscriber();
            await pub.PublishAsync(RedisChannel.Literal(channel), json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish timeline event {EventType} for {Symbol}", evt.EventType, evt.Symbol);
        }
    }
}
