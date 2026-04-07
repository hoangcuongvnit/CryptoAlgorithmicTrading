namespace FinancialLedger.Worker.Domain;

public static class LedgerEntryTypes
{
    public const string InitialFunding = "INITIAL_FUNDING";
    public const string BuyCashOut = "BUY_CASH_OUT";
    public const string SellCashIn = "SELL_CASH_IN";
    public const string RealizedPnl = "REALIZED_PNL";
    public const string Commission = "COMMISSION";
    public const string FundingFee = "FUNDING_FEE";
    public const string Withdrawal = "WITHDRAWAL";

    public static bool IsSupported(string type)
    {
        return type is InitialFunding or BuyCashOut or SellCashIn or RealizedPnl or Commission or FundingFee or Withdrawal;
    }
}

public sealed record VirtualAccountDto(Guid Id, string Environment, string BaseCurrency, DateTime CreatedAt);

public sealed record SessionDto(
    Guid Id,
    Guid AccountId,
    string AlgorithmName,
    decimal InitialBalance,
    DateTime StartTime,
    DateTime? EndTime,
    string Status);

public sealed record LedgerEntryDto(
    Guid Id,
    Guid SessionId,
    string? BinanceTransactionId,
    string Type,
    decimal Amount,
    string? Symbol,
    DateTime Timestamp,
    DateTime CreatedAt);

public sealed record PnlBreakdown(decimal RealizedPnl, decimal Commission, decimal FundingFee, decimal NetPnl);

public sealed record EquityHoldingSnapshot(
    string Symbol,
    decimal Quantity,
    decimal MarkPrice,
    decimal MarketValue);

public static class EquityEventTypes
{
    public const string SessionStart = "SESSION_START";
    public const string Buy = "BUY";
    public const string Sell = "SELL";
}

public sealed record SellEquitySnapshot(
    Guid SessionId,
    string TriggerTransactionId,
    string? TriggerSymbol,
    DateTime SnapshotTime,
    decimal CurrentBalance,
    decimal HoldingsMarketValue,
    decimal TotalEquity,
    IReadOnlyList<EquityHoldingSnapshot> Holdings,
    string EventType = EquityEventTypes.Sell);

public sealed record SellEquitySnapshotPoint(
    Guid SessionId,
    string TriggerTransactionId,
    string? TriggerSymbol,
    DateTime SnapshotTime,
    decimal CurrentBalance,
    decimal HoldingsMarketValue,
    decimal TotalEquity,
    IReadOnlyList<EquityHoldingSnapshot> Holdings,
    string EventType = EquityEventTypes.Sell);

public sealed record ResetSessionRequest(
    string AccountId,
    decimal NewInitialBalance,
    string AlgorithmName,
    bool ConfirmCloseAll = false,
    string? RequestedBy = null);
