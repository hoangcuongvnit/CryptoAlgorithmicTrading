using StackExchange.Redis;

namespace FinancialLedger.Worker.Services;

public sealed class SessionResetSagaService
{
    private const string TradingEngineCommandsChannel = "trading-engine:commands";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SessionResetSagaService> _logger;

    public SessionResetSagaService(IConnectionMultiplexer redis, ILogger<SessionResetSagaService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task RequestHaltAndCloseAllAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var command = $"HALT_AND_CLOSE_ALL:{accountId}";

        var subscriber = _redis.GetSubscriber();
        await subscriber.PublishAsync(RedisChannel.Literal(TradingEngineCommandsChannel), command);

        _logger.LogInformation(
            "Published reset command {Command} to {Channel}. Waiting for clear confirmation is handled by orchestrator integration.",
            command,
            TradingEngineCommandsChannel);
    }
}
