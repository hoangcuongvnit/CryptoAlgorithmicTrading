using CryptoTrading.Shared.Constants;
using StackExchange.Redis;
using Strategy.Worker.Infrastructure;
using System.Globalization;

namespace Strategy.Worker.Workers;

/// <summary>
/// Loads strategy config overrides from Redis on startup and subscribes to
/// strategy:config:changed for live updates without service restart.
/// </summary>
public sealed class StrategyConfigWatcher : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly StrategyConfigStore _store;
    private readonly ILogger<StrategyConfigWatcher> _logger;

    public StrategyConfigWatcher(
        IConnectionMultiplexer redis,
        StrategyConfigStore store,
        ILogger<StrategyConfigWatcher> logger)
    {
        _redis = redis;
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadFromRedisAsync();

        var sub = _redis.GetSubscriber();
        await sub.SubscribeAsync(
            RedisChannel.Literal(RedisChannels.StrategyConfigChanged),
            (_, _) =>
            {
                _ = Task.Run(async () =>
                {
                    await LoadFromRedisAsync();
                    _logger.LogInformation("Strategy config reloaded from Redis");
                });
            });

        try { await Task.Delay(Timeout.Infinite, stoppingToken); } catch (OperationCanceledException) { }
        await sub.UnsubscribeAsync(RedisChannel.Literal(RedisChannels.StrategyConfigChanged));
    }

    private async Task LoadFromRedisAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            var defaultVal = (string?)await db.StringGetAsync(RedisChannels.StrategyConfigDefaultOrderNotional);
            var minVal = (string?)await db.StringGetAsync(RedisChannels.StrategyConfigMinOrderNotional);

            decimal? defaultNotional = ParseDecimal(defaultVal);
            decimal? minNotional = ParseDecimal(minVal);

            if (defaultNotional.HasValue || minNotional.HasValue)
            {
                _store.Update(defaultNotional, minNotional);
                _logger.LogInformation(
                    "Strategy config loaded from Redis — DefaultOrderNotional: {Default}, MinOrderNotional: {Min}",
                    defaultNotional, minNotional);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load strategy config from Redis; using appsettings values");
        }
    }

    private static decimal? ParseDecimal(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
}
