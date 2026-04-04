namespace Executor.API.Infrastructure;

public sealed record OrderAmountLimitSnapshot(
    decimal MinOrderAmount,
    decimal MaxOrderAmount,
    DateTime UpdatedAtUtc);

public sealed class OrderAmountLimitStore
{
    private readonly object _sync = new();
    private OrderAmountLimitSnapshot _current;

    public OrderAmountLimitStore(decimal minOrderAmount, decimal maxOrderAmount)
    {
        _current = new OrderAmountLimitSnapshot(minOrderAmount, maxOrderAmount, DateTime.UtcNow);
    }

    public OrderAmountLimitSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public void Update(decimal minOrderAmount, decimal maxOrderAmount)
    {
        lock (_sync)
        {
            _current = new OrderAmountLimitSnapshot(minOrderAmount, maxOrderAmount, DateTime.UtcNow);
        }
    }
}