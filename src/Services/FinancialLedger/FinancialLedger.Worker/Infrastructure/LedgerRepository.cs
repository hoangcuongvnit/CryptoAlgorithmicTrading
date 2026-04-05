using Dapper;
using FinancialLedger.Worker.Domain;
using Npgsql;
using System.Data;

namespace FinancialLedger.Worker.Infrastructure;

public sealed class LedgerRepository
{
    private readonly string _connectionString;

    public LedgerRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");
    }

    public async Task<bool> InsertLedgerEntryAsync(
        Guid sessionId,
        string? binanceTransactionId,
        string type,
        decimal amount,
        string? symbol,
        DateTime timestamp,
        IDbTransaction? tx = null)
    {
        const string sql =
            """
            INSERT INTO ledger_entries (session_id, binance_transaction_id, type, amount, symbol, timestamp)
            VALUES (@SessionId, @BinanceTransactionId, @Type, @Amount, @Symbol, @Timestamp)
            ON CONFLICT (session_id, binance_transaction_id) WHERE binance_transaction_id IS NOT NULL DO NOTHING
            """;

        if (tx is not null)
        {
            var affectedInTx = await tx.Connection!.ExecuteAsync(sql, new
            {
                SessionId = sessionId,
                BinanceTransactionId = binanceTransactionId,
                Type = type,
                Amount = amount,
                Symbol = symbol,
                Timestamp = timestamp,
            }, tx);
            return affectedInTx > 0;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        var affected = await connection.ExecuteAsync(sql, new
        {
            SessionId = sessionId,
            BinanceTransactionId = binanceTransactionId,
            Type = type,
            Amount = amount,
            Symbol = symbol,
            Timestamp = timestamp,
        });

        return affected > 0;
    }

    public async Task<decimal> GetCurrentBalanceAsync(Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleAsync<decimal>(
            "SELECT COALESCE(SUM(amount), 0) FROM ledger_entries WHERE session_id = @SessionId",
            new { SessionId = sessionId });
    }

    public async Task<(IReadOnlyList<LedgerEntryDto> Entries, int Total)> GetLedgerEntriesAsync(
        Guid sessionId,
        DateTime? fromDate,
        DateTime? toDate,
        string? symbol,
        string? type,
        int page,
        int pageSize)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        var whereClause = "WHERE session_id = @SessionId";
        var parameters = new DynamicParameters();
        parameters.Add("SessionId", sessionId);

        if (fromDate.HasValue)
        {
            whereClause += " AND timestamp >= @FromDate";
            parameters.Add("FromDate", fromDate.Value);
        }

        if (toDate.HasValue)
        {
            whereClause += " AND timestamp <= @ToDate";
            parameters.Add("ToDate", toDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            whereClause += " AND symbol = @Symbol";
            parameters.Add("Symbol", symbol);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            whereClause += " AND type = @Type";
            parameters.Add("Type", type);
        }

        var total = await connection.QuerySingleAsync<int>(
            $"SELECT COUNT(*) FROM ledger_entries {whereClause}",
            parameters);

        var offset = (Math.Max(page, 1) - 1) * Math.Max(pageSize, 1);
        parameters.Add("PageSize", Math.Max(pageSize, 1));
        parameters.Add("Offset", offset);

        var entries = (await connection.QueryAsync<LedgerEntryDto>(
            $"""
            SELECT
                id AS Id,
                session_id AS SessionId,
                binance_transaction_id AS BinanceTransactionId,
                type AS Type,
                amount AS Amount,
                symbol AS Symbol,
                timestamp AS Timestamp,
                created_at AS CreatedAt
            FROM ledger_entries
            {whereClause}
            ORDER BY timestamp DESC
            LIMIT @PageSize OFFSET @Offset
            """,
            parameters)).ToList();

        return (entries, total);
    }

    public async Task<IReadOnlyDictionary<string, PnlBreakdown>> GetPnlBySymbolAsync(Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        var rows = await connection.QueryAsync<PnlRow>(
            """
            SELECT
                COALESCE(symbol, 'UNKNOWN') AS Symbol,
                COALESCE(SUM(CASE WHEN type = 'REALIZED_PNL' THEN amount ELSE 0 END), 0) AS RealizedPnl,
                COALESCE(SUM(CASE WHEN type = 'COMMISSION' THEN amount ELSE 0 END), 0) AS Commission,
                COALESCE(SUM(CASE WHEN type = 'FUNDING_FEE' THEN amount ELSE 0 END), 0) AS FundingFee
            FROM ledger_entries
            WHERE session_id = @SessionId
            GROUP BY COALESCE(symbol, 'UNKNOWN')
            """,
            new { SessionId = sessionId });

        return rows.ToDictionary(
            row => row.Symbol,
            row => new PnlBreakdown(
                row.RealizedPnl,
                row.Commission,
                row.FundingFee,
                row.RealizedPnl + row.Commission + row.FundingFee));
    }

    private sealed record PnlRow(string Symbol, decimal RealizedPnl, decimal Commission, decimal FundingFee);
}
