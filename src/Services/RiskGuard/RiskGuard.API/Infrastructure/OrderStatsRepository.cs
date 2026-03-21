using Dapper;
using Npgsql;

namespace RiskGuard.API.Infrastructure;

/// <summary>
/// Read-only queries against the Executor's orders table for risk calculations.
/// </summary>
public sealed class OrderStatsRepository
{
    private readonly string _connectionString;

    public OrderStatsRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Postgres connection string is required for RiskGuard.");
    }

    /// <summary>
    /// Returns today's total realized P&amp;L from CLOSED orders (UTC day boundary).
    /// Only orders with status='CLOSED' and a non-null realized_pnl are included.
    /// </summary>
    /// <param name="paperOnly">When true, only paper-trade orders are included.</param>
    public async Task<decimal> GetDailyNetPnlAsync(bool paperOnly = true, CancellationToken ct = default)
    {
        const string sql = """
            SELECT COALESCE(SUM(realized_pnl), 0)
            FROM public.orders
            WHERE status = 'CLOSED'
              AND realized_pnl IS NOT NULL
              AND (@paperOnly = false OR is_paper = true)
              AND time >= CURRENT_DATE
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.ExecuteScalarAsync<decimal>(
            new CommandDefinition(sql, new { paperOnly }, cancellationToken: ct));
    }
}
