using CryptoTrading.Shared.Constants;
using CryptoTrading.Shared.DTOs;
using StackExchange.Redis;

namespace Executor.API.Infrastructure;

public sealed class AuditStreamPublisher
{
    private readonly IConnectionMultiplexer _redis;

    public AuditStreamPublisher(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task PublishAsync(OrderRequest request, OrderResult result)
    {
        var db = _redis.GetDatabase();

        var entries = new NameValueEntry[]
        {
            new("event_version", "2"),
            new("order_id", result.OrderId),
            new("symbol", request.Symbol),
            new("side", request.Side.ToString()),
            new("order_type", request.Type.ToString()),
            new("filled_price", result.FilledPrice.ToString("0.########")),
            new("filled_qty", result.FilledQty.ToString("0.########")),
            new("trading_mode", result.IsTestnetTrade ? "testnet" : "live"),
            new("success", result.Success.ToString()),
            new("is_testnet", result.IsTestnetTrade.ToString()),
            new("error_code", result.ErrorCode == TradingErrorCode.None ? string.Empty : result.ErrorCode.ToString()),
            new("error_message", result.ErrorMessage ?? string.Empty),
            new("time", result.Timestamp.ToString("O")),
            new("session_id", request.SessionId ?? string.Empty),
            new("session_phase", request.SessionPhase?.ToString() ?? string.Empty),
            new("is_reduce_only", request.IsReduceOnly.ToString()),
            new("forced_liquidation", result.ForcedLiquidation.ToString()),
            new("liquidation_reason", result.LiquidationReason.ToString())
        };

        await db.StreamAddAsync(RedisChannels.TradesAudit, entries);
    }
}
