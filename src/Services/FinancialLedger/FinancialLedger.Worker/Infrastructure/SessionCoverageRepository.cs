using Dapper;
using Npgsql;

namespace FinancialLedger.Worker.Infrastructure;

public sealed class SessionCoverageRepository
{
    private readonly string _connectionString;

    public SessionCoverageRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");
    }

    public async Task<SessionCoverageResult?> GetSessionCoverageAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var session = await connection.QuerySingleOrDefaultAsync<SessionWindow>(new CommandDefinition(
            """
            SELECT
                id AS SessionId,
                start_time AS StartTime,
                COALESCE(end_time, NOW()) AS EndTime,
                status AS Status
            FROM test_sessions
            WHERE id = @SessionId
            LIMIT 1
            """,
            new { SessionId = sessionId },
            cancellationToken: cancellationToken));

        if (session is null)
        {
            return null;
        }

        var orders = await connection.QuerySingleAsync<OrderCoverageRow>(new CommandDefinition(
            """
            SELECT
                COUNT(*) FILTER (WHERE success = TRUE) AS TotalSuccessfulOrders,
                COUNT(*) FILTER (WHERE success = TRUE AND LOWER(side) = 'buy') AS SuccessfulBuyOrders,
                COUNT(*) FILTER (WHERE success = TRUE AND LOWER(side) = 'sell') AS SuccessfulSellOrders
            FROM orders
            WHERE time >= @StartTime
              AND time <= @EndTime
            """,
            new { session.StartTime, session.EndTime },
            cancellationToken: cancellationToken));

        var ledger = await connection.QuerySingleAsync<LedgerCoverageRow>(new CommandDefinition(
            """
            SELECT
                COUNT(*) FILTER (WHERE type = 'BUY_CASH_OUT') AS BuyCashOutCount,
                COUNT(*) FILTER (WHERE type = 'SELL_CASH_IN') AS SellCashInCount,
                COUNT(*) FILTER (WHERE type = 'REALIZED_PNL') AS RealizedPnlCount,
                COUNT(*) FILTER (WHERE type = 'COMMISSION' AND binance_transaction_id ILIKE '%:COMMISSION:BUY') AS CommissionBuyCount,
                COUNT(*) FILTER (WHERE type = 'COMMISSION' AND binance_transaction_id ILIKE '%:COMMISSION:SELL') AS CommissionSellCount,
                COUNT(*) FILTER (
                    WHERE type = 'COMMISSION'
                      AND (
                            binance_transaction_id IS NULL
                            OR (
                                binance_transaction_id NOT ILIKE '%:COMMISSION:BUY'
                                AND binance_transaction_id NOT ILIKE '%:COMMISSION:SELL'
                            )
                      )
                ) AS CommissionUnknownCount,
                COUNT(*) AS TotalLedgerEntries
            FROM ledger_entries
            WHERE session_id = @SessionId
            """,
            new { SessionId = sessionId },
            cancellationToken: cancellationToken));

        var buyOrders = orders.SuccessfulBuyOrders;
        var sellOrders = orders.SuccessfulSellOrders;

        return new SessionCoverageResult
        {
            SessionId = session.SessionId,
            SessionStatus = session.Status,
            SessionStartTime = session.StartTime,
            SessionEndTime = session.EndTime,
            Method = "time-window-order-vs-ledger",
            Note = "Orders are correlated by session time window. Direct orderId-to-transactionId matching is unavailable in current schema.",
            Orders = new OrderCoverageSummary
            {
                TotalSuccessfulOrders = orders.TotalSuccessfulOrders,
                SuccessfulBuyOrders = buyOrders,
                SuccessfulSellOrders = sellOrders,
            },
            Ledger = new LedgerCoverageSummary
            {
                TotalLedgerEntries = ledger.TotalLedgerEntries,
                BuyCashOutCount = ledger.BuyCashOutCount,
                SellCashInCount = ledger.SellCashInCount,
                RealizedPnlCount = ledger.RealizedPnlCount,
                CommissionBuyCount = ledger.CommissionBuyCount,
                CommissionSellCount = ledger.CommissionSellCount,
                CommissionUnknownCount = ledger.CommissionUnknownCount,
            },
            Coverage = new CoverageSummary
            {
                BuyCashOutCoveragePercent = CalcCoveragePercent(buyOrders, ledger.BuyCashOutCount),
                BuyCommissionCoveragePercent = CalcCoveragePercent(buyOrders, ledger.CommissionBuyCount),
                SellCashInCoveragePercent = CalcCoveragePercent(sellOrders, ledger.SellCashInCount),
                SellCommissionCoveragePercent = CalcCoveragePercent(sellOrders, ledger.CommissionSellCount),
                RealizedPnlCoveragePercent = CalcCoveragePercent(sellOrders, ledger.RealizedPnlCount),
                MissingBuyCashOut = Math.Max(0, buyOrders - ledger.BuyCashOutCount),
                MissingBuyCommission = Math.Max(0, buyOrders - ledger.CommissionBuyCount),
                MissingSellCashIn = Math.Max(0, sellOrders - ledger.SellCashInCount),
                MissingSellCommission = Math.Max(0, sellOrders - ledger.CommissionSellCount),
                MissingRealizedPnl = Math.Max(0, sellOrders - ledger.RealizedPnlCount),
            }
        };
    }

    private static decimal CalcCoveragePercent(int expected, int actual)
    {
        if (expected <= 0)
        {
            return 100m;
        }

        var ratio = Math.Min(1m, (decimal)actual / expected);
        return decimal.Round(ratio * 100m, 2);
    }

    private sealed record SessionWindow(Guid SessionId, DateTime StartTime, DateTime EndTime, string Status);

    private sealed record OrderCoverageRow(int TotalSuccessfulOrders, int SuccessfulBuyOrders, int SuccessfulSellOrders);

    private sealed record LedgerCoverageRow(
        int TotalLedgerEntries,
        int BuyCashOutCount,
        int SellCashInCount,
        int RealizedPnlCount,
        int CommissionBuyCount,
        int CommissionSellCount,
        int CommissionUnknownCount);
}

public sealed class SessionCoverageResult
{
    public Guid SessionId { get; init; }
    public string SessionStatus { get; init; } = string.Empty;
    public DateTime SessionStartTime { get; init; }
    public DateTime SessionEndTime { get; init; }
    public string Method { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public OrderCoverageSummary Orders { get; init; } = new();
    public LedgerCoverageSummary Ledger { get; init; } = new();
    public CoverageSummary Coverage { get; init; } = new();
}

public sealed class OrderCoverageSummary
{
    public int TotalSuccessfulOrders { get; init; }
    public int SuccessfulBuyOrders { get; init; }
    public int SuccessfulSellOrders { get; init; }
}

public sealed class LedgerCoverageSummary
{
    public int TotalLedgerEntries { get; init; }
    public int BuyCashOutCount { get; init; }
    public int SellCashInCount { get; init; }
    public int RealizedPnlCount { get; init; }
    public int CommissionBuyCount { get; init; }
    public int CommissionSellCount { get; init; }
    public int CommissionUnknownCount { get; init; }
}

public sealed class CoverageSummary
{
    public decimal BuyCashOutCoveragePercent { get; init; }
    public decimal BuyCommissionCoveragePercent { get; init; }
    public decimal SellCashInCoveragePercent { get; init; }
    public decimal SellCommissionCoveragePercent { get; init; }
    public decimal RealizedPnlCoveragePercent { get; init; }
    public int MissingBuyCashOut { get; init; }
    public int MissingBuyCommission { get; init; }
    public int MissingSellCashIn { get; init; }
    public int MissingSellCommission { get; init; }
    public int MissingRealizedPnl { get; init; }
}
