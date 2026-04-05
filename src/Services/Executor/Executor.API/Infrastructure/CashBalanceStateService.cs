namespace Executor.API.Infrastructure;

public sealed class CashBalanceStateService
{
    private readonly object _gate = new();
    private CashBalanceSnapshot? _mainnetSnapshot;

    public sealed record CashBalanceSnapshot(decimal CashBalance, DateTime UpdatedAtUtc, string Source);

    public void UpdateMainnetSnapshot(decimal cashBalance, DateTime updatedAtUtc, string source)
    {
        if (cashBalance < 0m)
            cashBalance = 0m;

        lock (_gate)
        {
            _mainnetSnapshot = new CashBalanceSnapshot(cashBalance, updatedAtUtc, source);
        }
    }

    public bool TryGetMainnetSnapshot(out CashBalanceSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_mainnetSnapshot is null)
            {
                snapshot = new CashBalanceSnapshot(0m, DateTime.MinValue, string.Empty);
                return false;
            }

            snapshot = _mainnetSnapshot;
            return true;
        }
    }
}