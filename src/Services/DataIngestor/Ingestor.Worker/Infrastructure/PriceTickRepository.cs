using CryptoTrading.Shared.Database;
using CryptoTrading.Shared.DTOs;
using Dapper;
using Npgsql;

namespace Ingestor.Worker.Infrastructure;

public sealed class PriceTickRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PriceTickRepository> _logger;

    public PriceTickRepository(IConfiguration configuration, ILogger<PriceTickRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured");
        _logger = logger;
    }

    /// <summary>
    /// Inserts real-time price ticks into yearly partitioned tables.
    /// Automatically routes data to the correct table based on timestamp.
    /// </summary>
    public async Task<int> UpsertBatchAsync(IReadOnlyCollection<PriceTick> ticks, CancellationToken cancellationToken)
    {
        if (ticks.Count == 0)
        {
            return 0;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Group by year to ensure we insert into correct yearly tables
            var groupedByYear = PriceTicksTableHelper.GroupByYear(ticks);
            var totalInserted = 0;

            foreach (var (year, yearTicks) in groupedByYear)
            {
                // Ensure table exists for this year (auto-creates if needed)
                await PriceTicksTableHelper.EnsureTableExistsAsync(connection, yearTicks[0].Timestamp, cancellationToken);

                // Generate INSERT SQL for this year's table
                var insertSql = PriceTicksTableHelper.GenerateInsertSql(year);

                // Execute batch insert
                var inserted = await connection.ExecuteAsync(new CommandDefinition(
                    insertSql,
                    yearTicks,
                    transaction: transaction,
                    cancellationToken: cancellationToken));

                totalInserted += inserted;
            }

            await transaction.CommitAsync(cancellationToken);
            return totalInserted;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to write {Count} price ticks batch", ticks.Count);
            throw;
        }
    }
}
