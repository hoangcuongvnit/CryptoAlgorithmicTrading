using Dapper;
using Npgsql;
using CryptoTrading.Shared.DTOs;

namespace Ingestor.Worker.Infrastructure;

public sealed class PriceTickRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PriceTickRepository> _logger;

    private const string InsertSql = """
        INSERT INTO price_ticks (time, symbol, price, volume, open, high, low, close, interval)
        VALUES (@Timestamp, @Symbol, @Price, @Volume, @Open, @High, @Low, @Close, @Interval)
        ON CONFLICT (time, symbol, interval) DO NOTHING;
        """;

    public PriceTickRepository(IConfiguration configuration, ILogger<PriceTickRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured");
        _logger = logger;
    }

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
            var rows = await connection.ExecuteAsync(new CommandDefinition(
                InsertSql,
                ticks,
                transaction: transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return rows;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to write {Count} price ticks batch", ticks.Count);
            throw;
        }
    }
}
