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
            new("event_version", "1"),
            new("order_id", result.OrderId),
            new("symbol", request.Symbol),
            new("side", request.Side.ToString()),
            new("order_type", request.Type.ToString()),
            new("filled_price", result.FilledPrice.ToString("0.########")),
            new("filled_qty", result.FilledQty.ToString("0.########")),
            new("is_paper", result.IsPaperTrade.ToString()),
            new("success", result.Success.ToString()),
            new("error_code", string.IsNullOrWhiteSpace(result.ErrorMessage) ? string.Empty : "EXECUTION_ERROR"),
            new("error_message", result.ErrorMessage ?? string.Empty),
            new("time", result.Timestamp.ToString("O"))
        };

        await db.StreamAddAsync(RedisChannels.TradesAudit, entries);
    }
}
