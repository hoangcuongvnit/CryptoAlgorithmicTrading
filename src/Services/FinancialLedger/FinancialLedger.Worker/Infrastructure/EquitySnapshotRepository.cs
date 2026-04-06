using Dapper;
using FinancialLedger.Worker.Domain;
using Npgsql;
using System.Text;
using System.Text.Json;

namespace FinancialLedger.Worker.Infrastructure;

public sealed class EquitySnapshotRepository
{
    private readonly string _connectionString;

    public EquitySnapshotRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");
    }

    public async Task<bool> InsertSellSnapshotAsync(SellEquitySnapshot snapshot)
    {
        const string sql =
            """
            INSERT INTO ledger_equity_snapshots (
                session_id,
                trigger_transaction_id,
                trigger_symbol,
                snapshot_time,
                current_balance,
                holdings_market_value,
                total_equity,
                holdings_json,
                event_type)
            VALUES (
                @SessionId,
                @TriggerTransactionId,
                @TriggerSymbol,
                @SnapshotTime,
                @CurrentBalance,
                @HoldingsMarketValue,
                @TotalEquity,
                CAST(@HoldingsJson AS jsonb),
                @EventType)
            ON CONFLICT (session_id, trigger_transaction_id) DO NOTHING
            """;

        var holdingsJson = JsonSerializer.Serialize(snapshot.Holdings);

        await using var connection = new NpgsqlConnection(_connectionString);
        int affected;
        try
        {
            affected = await connection.ExecuteAsync(sql, new
            {
                snapshot.SessionId,
                snapshot.TriggerTransactionId,
                snapshot.TriggerSymbol,
                snapshot.SnapshotTime,
                snapshot.CurrentBalance,
                snapshot.HoldingsMarketValue,
                snapshot.TotalEquity,
                HoldingsJson = holdingsJson,
                snapshot.EventType,
            });
        }
        catch (PostgresException ex) when (IsMissingSnapshotTable(ex))
        {
            // Backward compatibility: environments that have not run the equity snapshot migration yet.
            return false;
        }

        return affected > 0;
    }

    public async Task<IReadOnlyList<SellEquitySnapshotPoint>> GetSellTimelineAsync(
        Guid sessionId,
        DateTime? fromDate,
        DateTime? toDate,
        int limit)
    {
        var sqlBuilder = new StringBuilder(
            """
            SELECT
                session_id AS SessionId,
                trigger_transaction_id AS TriggerTransactionId,
                trigger_symbol AS TriggerSymbol,
                snapshot_time AS SnapshotTime,
                current_balance AS CurrentBalance,
                holdings_market_value AS HoldingsMarketValue,
                total_equity AS TotalEquity,
                holdings_json AS HoldingsJson,
                event_type AS EventType
            FROM ledger_equity_snapshots
            WHERE session_id = @SessionId
            """);
        sqlBuilder.AppendLine();

        var parameters = new DynamicParameters();
        parameters.Add("SessionId", sessionId);

        if (fromDate.HasValue)
        {
            sqlBuilder.AppendLine("  AND snapshot_time >= @FromDate");
            parameters.Add("FromDate", fromDate.Value);
        }

        if (toDate.HasValue)
        {
            sqlBuilder.AppendLine("  AND snapshot_time <= @ToDate");
            parameters.Add("ToDate", toDate.Value);
        }

        sqlBuilder.AppendLine("ORDER BY snapshot_time ASC");
        sqlBuilder.AppendLine("LIMIT @Limit");

        parameters.Add("Limit", Math.Clamp(limit, 1, 5000));

        await using var connection = new NpgsqlConnection(_connectionString);
        IEnumerable<SellTimelineRow> rows;
        try
        {
            rows = await connection.QueryAsync<SellTimelineRow>(sqlBuilder.ToString(), parameters);
        }
        catch (PostgresException ex) when (IsMissingSnapshotTable(ex))
        {
            // Treat as no timeline data when migration has not been applied yet.
            return [];
        }

        return rows.Select(static row => new SellEquitySnapshotPoint(
            row.SessionId,
            row.TriggerTransactionId,
            row.TriggerSymbol,
            row.SnapshotTime,
            row.CurrentBalance,
            row.HoldingsMarketValue,
            row.TotalEquity,
            DeserializeHoldings(row.HoldingsJson),
            row.EventType)).ToList();
    }

    private static IReadOnlyList<EquityHoldingSnapshot> DeserializeHoldings(string holdingsJson)
    {
        if (string.IsNullOrWhiteSpace(holdingsJson))
        {
            return [];
        }

        var holdings = JsonSerializer.Deserialize<List<EquityHoldingSnapshot>>(holdingsJson);
        return holdings ?? [];
    }

    private sealed record SellTimelineRow(
        Guid SessionId,
        string TriggerTransactionId,
        string? TriggerSymbol,
        DateTime SnapshotTime,
        decimal CurrentBalance,
        decimal HoldingsMarketValue,
        decimal TotalEquity,
        string HoldingsJson,
        string EventType);

    private static bool IsMissingSnapshotTable(PostgresException ex)
    {
        const string undefinedTableSqlState = "42P01";
        return string.Equals(ex.SqlState, undefinedTableSqlState, StringComparison.Ordinal);
    }
}
