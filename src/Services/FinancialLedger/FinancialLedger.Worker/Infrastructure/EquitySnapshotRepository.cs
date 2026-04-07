using Dapper;
using FinancialLedger.Worker.Domain;
using Npgsql;
using System.Text;
using System.Text.Json;

namespace FinancialLedger.Worker.Infrastructure;

public sealed class EquitySnapshotRepository
{
    private readonly string _connectionString;
    private const string UndefinedTableSqlState = "42P01";
    private const string UndefinedColumnSqlState = "42703";

    public EquitySnapshotRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");
    }

    public async Task<bool> InsertSellSnapshotAsync(SellEquitySnapshot snapshot)
    {
        const string sqlWithEventType =
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
            ON CONFLICT (session_id, trigger_transaction_id)
            DO UPDATE SET
                trigger_symbol = EXCLUDED.trigger_symbol,
                snapshot_time = EXCLUDED.snapshot_time,
                current_balance = EXCLUDED.current_balance,
                holdings_market_value = EXCLUDED.holdings_market_value,
                total_equity = EXCLUDED.total_equity,
                holdings_json = EXCLUDED.holdings_json,
                event_type = EXCLUDED.event_type
            """;

        const string sqlLegacy =
            """
            INSERT INTO ledger_equity_snapshots (
                session_id,
                trigger_transaction_id,
                trigger_symbol,
                snapshot_time,
                current_balance,
                holdings_market_value,
                total_equity,
                holdings_json)
            VALUES (
                @SessionId,
                @TriggerTransactionId,
                @TriggerSymbol,
                @SnapshotTime,
                @CurrentBalance,
                @HoldingsMarketValue,
                @TotalEquity,
                CAST(@HoldingsJson AS jsonb))
            ON CONFLICT (session_id, trigger_transaction_id)
            DO UPDATE SET
                trigger_symbol = EXCLUDED.trigger_symbol,
                snapshot_time = EXCLUDED.snapshot_time,
                current_balance = EXCLUDED.current_balance,
                holdings_market_value = EXCLUDED.holdings_market_value,
                total_equity = EXCLUDED.total_equity,
                holdings_json = EXCLUDED.holdings_json
            """;

        var holdingsJson = JsonSerializer.Serialize(snapshot.Holdings);

        await using var connection = new NpgsqlConnection(_connectionString);
        int affected;
        try
        {
            affected = await connection.ExecuteAsync(sqlWithEventType, new
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
        catch (PostgresException ex) when (IsMissingEventTypeColumn(ex))
        {
            affected = await connection.ExecuteAsync(sqlLegacy, new
            {
                snapshot.SessionId,
                snapshot.TriggerTransactionId,
                snapshot.TriggerSymbol,
                snapshot.SnapshotTime,
                snapshot.CurrentBalance,
                snapshot.HoldingsMarketValue,
                snapshot.TotalEquity,
                HoldingsJson = holdingsJson,
            });
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
        catch (PostgresException ex) when (IsMissingEventTypeColumn(ex))
        {
            rows = await QueryLegacyTimelineAsync(connection, sessionId, fromDate, toDate, limit);
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

    private static async Task<IEnumerable<SellTimelineRow>> QueryLegacyTimelineAsync(
        NpgsqlConnection connection,
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
                CASE
                    WHEN trigger_transaction_id LIKE 'SESSION_START:%' THEN 'SESSION_START'
                    WHEN trigger_transaction_id LIKE '%:COMMISSION:BUY' THEN 'BUY'
                    WHEN trigger_transaction_id LIKE '%:COMMISSION:SELL' THEN 'SELL'
                    WHEN trigger_transaction_id LIKE '%:REALIZED_PNL' THEN 'SELL'
                    ELSE 'SELL'
                END AS EventType
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
        return await connection.QueryAsync<SellTimelineRow>(sqlBuilder.ToString(), parameters);
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
        return string.Equals(ex.SqlState, UndefinedTableSqlState, StringComparison.Ordinal);
    }

    private static bool IsMissingEventTypeColumn(PostgresException ex)
    {
        return string.Equals(ex.SqlState, UndefinedColumnSqlState, StringComparison.Ordinal)
            && ex.MessageText.Contains("event_type", StringComparison.OrdinalIgnoreCase);
    }
}
