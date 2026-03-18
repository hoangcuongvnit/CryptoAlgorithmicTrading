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
    /// Returns today's net cash flow from successful orders (UTC day boundary):
    ///   Σ(sell revenue) − Σ(buy cost)
    /// A negative result means more was spent than received — i.e. a net loss position.
    /// </summary>
    /// <param name="paperOnly">When true, only paper-trade orders are included.</param>
    public async Task<decimal> GetDailyNetPnlAsync(bool paperOnly = true, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                COALESCE(SUM(CASE WHEN side = 'Sell' THEN filled_qty * filled_price ELSE 0 END), 0)
              - COALESCE(SUM(CASE WHEN side = 'Buy'  THEN filled_qty * filled_price ELSE 0 END), 0)
            FROM orders
            WHERE success = true
              AND (is_paper = @paperOnly OR @paperOnly = false)
              AND time >= (CURRENT_DATE AT TIME ZONE 'UTC')
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.ExecuteScalarAsync<decimal>(
            new CommandDefinition(sql, new { paperOnly }, cancellationToken: ct));
    }
}
