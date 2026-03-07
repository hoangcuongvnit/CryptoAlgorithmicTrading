using Dapper;
using Npgsql;

namespace Executor.API.Infrastructure;

public sealed class PriceReferenceRepository
{
    private readonly string _connectionString;

    public PriceReferenceRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured");
    }

    public async Task<decimal?> GetLatestClosePriceAsync(string symbol, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT close
            FROM price_ticks
            WHERE symbol = @Symbol
              AND interval = '1m'
              AND close IS NOT NULL
            ORDER BY time DESC
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<decimal?>(
            new CommandDefinition(sql, new { Symbol = symbol }, cancellationToken: cancellationToken));
    }
}
