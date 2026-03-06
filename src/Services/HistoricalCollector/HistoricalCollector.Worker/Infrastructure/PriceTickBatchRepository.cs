using CryptoTrading.Shared.DTOs;
using Dapper;
using HistoricalCollector.Worker.Models;
using Npgsql;

namespace HistoricalCollector.Worker.Infrastructure;

public sealed class PriceTickBatchRepository
{
    private readonly string _connectionString;

    private const string InsertSql = """
        INSERT INTO price_ticks (time, symbol, price, volume, open, high, low, close, interval)
        VALUES (@Timestamp, @Symbol, @Price, @Volume, @Open, @High, @Low, @Close, @Interval)
        ON CONFLICT (time, symbol, interval) DO NOTHING;
        """;

    public PriceTickBatchRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured");
    }

    public async Task<int> UpsertBatchAsync(IReadOnlyCollection<PriceTick> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return 0;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(InsertSql, batch, cancellationToken: cancellationToken));
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

        const string sql = """
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
            counts AS (
                SELECT
                    p.symbol,
                    date_trunc('day', p.time) AS day_start,
                    count(*) AS candle_count
                FROM price_ticks p
                WHERE p.interval = @Interval
                  AND p.symbol = ANY(@Symbols)
                  AND p.time >= date_trunc('day', @StartDateUtc::timestamptz)
                  AND p.time < date_trunc('day', @EndDateUtc::timestamptz) + interval '1 day'
                GROUP BY p.symbol, date_trunc('day', p.time)
            )
            INSERT INTO data_gaps (symbol, interval, gap_start, gap_end, detected_at)
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
                FROM data_gaps g
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
        const string sql = """
            SELECT
                id,
                symbol,
                interval,
                gap_start AS GapStart,
                gap_end AS GapEnd,
                detected_at AS DetectedAt,
                filled_at AS FilledAt
            FROM data_gaps
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
        const string sql = """
            UPDATE data_gaps
            SET filled_at = NOW()
            WHERE id = @Id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }
}
