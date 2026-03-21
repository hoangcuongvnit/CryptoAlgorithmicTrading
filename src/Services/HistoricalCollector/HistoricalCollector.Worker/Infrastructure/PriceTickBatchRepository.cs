using CryptoTrading.Shared.Database;
using CryptoTrading.Shared.DTOs;
using Dapper;
using HistoricalCollector.Worker.Models;
using Npgsql;

namespace HistoricalCollector.Worker.Infrastructure;

public sealed class PriceTickBatchRepository
{
    private readonly string _connectionString;

    public PriceTickBatchRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured");
    }

    /// <summary>
    /// Inserts a batch of price ticks into yearly partitioned tables.
    /// Automatically routes data to the correct table based on timestamp.
    /// </summary>
    public async Task<int> UpsertBatchAsync(IReadOnlyCollection<PriceTick> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return 0;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Group by year to ensure we insert into correct yearly tables
        var groupedByYear = PriceTicksTableHelper.GroupByYear(batch);
        var totalInserted = 0;

        foreach (var (year, ticks) in groupedByYear)
        {
            // Ensure table exists for this year (auto-creates if needed)
            await PriceTicksTableHelper.EnsureTableExistsAsync(connection, ticks[0].Timestamp, cancellationToken);

            // Generate INSERT SQL for this year's table
            var insertSql = PriceTicksTableHelper.GenerateInsertSql(year);

            // Execute batch insert
            var inserted = await connection.ExecuteAsync(
                new CommandDefinition(insertSql, ticks, cancellationToken: cancellationToken));

            totalInserted += inserted;
        }

        return totalInserted;
    }

    public async Task<int> DetectAndStoreDailyGapsAsync(
        IReadOnlyCollection<string> symbols,
        string interval,
        DateTime startDateUtc,
        DateTime endDateUtc,
        int expectedCandlesPerDay,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0)
        {
            return 0;
        }

        // Determine which yearly tables we need to query
        var startYear = startDateUtc.Year;
        var endYear = endDateUtc.Year;

        // Build UNION ALL query across relevant yearly tables
        var tableQueries = new List<string>();
        for (var year = startYear; year <= endYear; year++)
        {
            var tableName = PriceTicksTableHelper.GetTableNameForYear(year);
            tableQueries.Add($@"
                SELECT symbol, time, interval
                FROM {tableName}
                WHERE interval = @Interval
                  AND symbol = ANY(@Symbols)
                  AND time >= date_trunc('day', @StartDateUtc::timestamptz)
                  AND time < date_trunc('day', @EndDateUtc::timestamptz) + interval '1 day'
            ");
        }

        var unionQuery = string.Join(" UNION ALL ", tableQueries);
        var dataGapsTable = PriceTicksTableHelper.GetDataGapsTableName();

        var sql = $"""
            WITH days AS (
                SELECT generate_series(
                    date_trunc('day', @StartDateUtc::timestamptz),
                    date_trunc('day', @EndDateUtc::timestamptz),
                    interval '1 day') AS day_start
            ),
            symbol_days AS (
                SELECT s.symbol, d.day_start
                FROM unnest(@Symbols) AS s(symbol)
                CROSS JOIN days d
            ),
            all_data AS (
                {unionQuery}
            ),
            counts AS (
                SELECT
                    p.symbol,
                    date_trunc('day', p.time) AS day_start,
                    count(*) AS candle_count
                FROM all_data p
                GROUP BY p.symbol, date_trunc('day', p.time)
            )
            INSERT INTO {dataGapsTable} (symbol, interval, gap_start, gap_end, detected_at)
            SELECT
                sd.symbol,
                @Interval,
                sd.day_start,
                sd.day_start + interval '1 day' - interval '1 minute',
                NOW()
            FROM symbol_days sd
            LEFT JOIN counts c
              ON c.symbol = sd.symbol
             AND c.day_start = sd.day_start
            WHERE COALESCE(c.candle_count, 0) < @ExpectedCandlesPerDay
              AND NOT EXISTS (
                SELECT 1
                FROM {dataGapsTable} g
                WHERE g.symbol = sd.symbol
                  AND g.interval = @Interval
                  AND g.gap_start = sd.day_start
                  AND g.gap_end = sd.day_start + interval '1 day' - interval '1 minute'
                  AND g.filled_at IS NULL
              );
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Symbols = symbols.ToArray(),
                Interval = interval,
                StartDateUtc = startDateUtc,
                EndDateUtc = endDateUtc,
                ExpectedCandlesPerDay = expectedCandlesPerDay
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<DataGap>> GetOpenGapsAsync(string interval, int maxRows, CancellationToken cancellationToken)
    {
        var dataGapsTable = PriceTicksTableHelper.GetDataGapsTableName();

        var sql = $"""
            SELECT
                id,
                symbol,
                interval,
                gap_start AS GapStart,
                gap_end AS GapEnd,
                detected_at AS DetectedAt,
                filled_at AS FilledAt
            FROM {dataGapsTable}
            WHERE filled_at IS NULL
              AND interval = @Interval
            ORDER BY gap_start ASC
            LIMIT @MaxRows;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<DataGap>(new CommandDefinition(
            sql,
            new { Interval = interval, MaxRows = maxRows },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task MarkGapFilledAsync(long id, CancellationToken cancellationToken)
    {
        var dataGapsTable = PriceTicksTableHelper.GetDataGapsTableName();

        var sql = $"""
            UPDATE {dataGapsTable}
            SET filled_at = NOW()
            WHERE id = @Id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }
}
