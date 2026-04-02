using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Data;

namespace Gateway.API.Dashboard;

public sealed class DashboardQueryService : IDashboardQueryService
{
    private static readonly IReadOnlyDictionary<string, string> BucketByInterval = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["1m"] = "time",
        ["5m"] = "date_bin(INTERVAL '5 minutes', time, TIMESTAMPTZ '2000-01-01')",
        ["15m"] = "date_bin(INTERVAL '15 minutes', time, TIMESTAMPTZ '2000-01-01')",
        ["1h"] = "date_bin(INTERVAL '1 hour', time, TIMESTAMPTZ '2000-01-01')",
        ["1d"] = "date_bin(INTERVAL '1 day', time, TIMESTAMPTZ '2000-01-01')"
    };

    private readonly string _connectionString;
    private readonly DashboardOptions _options;
    private readonly IMemoryCache _cache;

    public DashboardQueryService(IConfiguration configuration, IOptions<DashboardOptions> options, IMemoryCache cache)
    {
        _options = options.Value;
        _cache = cache;
        _connectionString = configuration.GetConnectionString(_options.ConnectionStringName)
            ?? throw new InvalidOperationException($"Missing connection string '{_options.ConnectionStringName}'.");
    }

    public async Task<OverviewResponse> GetOverviewAsync(
        DateTime startUtc,
        DateTime endUtc,
        string[] symbols,
        string interval,
        CancellationToken cancellationToken)
    {
        var normalizedInterval = NormalizeInterval(interval);
        var cacheKey = BuildCacheKey("overview", startUtc, endUtc, normalizedInterval, symbols, 0, 0, null);

        if (_cache.TryGetValue(cacheKey, out OverviewResponse? cached) && cached is not null)
        {
            return cached;
        }

        var expectedPerDay = ExpectedCandlesPerDay(normalizedInterval);

        await using var connection = await OpenConnectionAsync(cancellationToken);

        const string symbolStatsSql = """
            SELECT symbol AS Symbol,
                   COUNT(*) AS RowCount,
                   MAX(time) AS LatestTimeUtc
            FROM historical_collector.price_ticks
            WHERE interval = @Interval
              AND symbol = ANY(@Symbols)
              AND time >= @StartUtc
              AND time < @EndUtc
            GROUP BY symbol
            ORDER BY symbol;
            """;

        var stats = (await connection.QueryAsync<OverviewRawRow>(new CommandDefinition(
            symbolStatsSql,
            new { Interval = normalizedInterval, Symbols = symbols, StartUtc = startUtc, EndUtc = endUtc },
            cancellationToken: cancellationToken))).ToDictionary(row => row.Symbol, StringComparer.OrdinalIgnoreCase);

        const string dailyCountsSql = """
            SELECT symbol AS Symbol,
                   date_trunc('day', time)::date AS Day,
                   COUNT(*)::int AS Candles
            FROM historical_collector.price_ticks
            WHERE interval = @Interval
              AND symbol = ANY(@Symbols)
              AND time >= @StartUtc
              AND time < @EndUtc
            GROUP BY symbol, date_trunc('day', time)::date
            ORDER BY symbol, Day;
            """;

        var dailyCounts = (await connection.QueryAsync<DailyRawRow>(new CommandDefinition(
            dailyCountsSql,
            new { Interval = normalizedInterval, Symbols = symbols, StartUtc = startUtc, EndUtc = endUtc },
            cancellationToken: cancellationToken))).ToList();

        const string openGapsSql = """
            SELECT COUNT(*)::int
            FROM historical_collector.data_gaps
            WHERE interval = @Interval
              AND symbol = ANY(@Symbols)
              AND filled_at IS NULL;
            """;

        var totalOpenGaps = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            openGapsSql,
            new { Interval = normalizedInterval, Symbols = symbols },
            cancellationToken: cancellationToken));

        var totalDays = Math.Max(1, (int)Math.Ceiling((endUtc.Date - startUtc.Date).TotalDays));

        var groupedCounts = dailyCounts
            .GroupBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToDictionary(row => row.Day, row => row.Candles), StringComparer.OrdinalIgnoreCase);

        var nowUtc = DateTime.UtcNow;
        var rows = new List<OverviewSymbolRow>(symbols.Length);

        foreach (var symbol in symbols)
        {
            stats.TryGetValue(symbol, out var statRow);
            groupedCounts.TryGetValue(symbol, out var byDay);

            var daysBelowExpected = 0;
            var actualTotal = 0;

            for (var day = DateOnly.FromDateTime(startUtc.Date); day < DateOnly.FromDateTime(endUtc.Date); day = day.AddDays(1))
            {
                var actual = byDay is not null && byDay.TryGetValue(day, out var value) ? value : 0;
                actualTotal += actual;
                if (actual < expectedPerDay)
                {
                    daysBelowExpected++;
                }
            }

            var expectedTotal = totalDays * expectedPerDay;
            var coverage = expectedTotal == 0 ? 0d : Math.Round((double)actualTotal / expectedTotal * 100d, 2);
            var freshness = statRow?.LatestTimeUtc is null ? (double?)null : Math.Round((nowUtc - statRow.LatestTimeUtc.Value).TotalMinutes, 2);

            rows.Add(new OverviewSymbolRow(
                symbol,
                statRow?.RowCount ?? 0,
                statRow?.LatestTimeUtc,
                freshness,
                coverage,
                daysBelowExpected));
        }

        var result = new OverviewResponse(startUtc, endUtc, normalizedInterval, rows, totalOpenGaps);
        _cache.Set(cacheKey, result, TimeSpan.FromSeconds(_options.CacheSeconds));
        return result;
    }

    public async Task<CandlesResponse> GetCandlesAsync(
        string symbol,
        DateTime startUtc,
        DateTime endUtc,
        string interval,
        string[] comparisonSymbols,
        CancellationToken cancellationToken)
    {
        var normalizedInterval = NormalizeInterval(interval);
        var cacheKey = BuildCacheKey("candles", startUtc, endUtc, normalizedInterval, comparisonSymbols, 0, 0, symbol);

        if (_cache.TryGetValue(cacheKey, out CandlesResponse? cached) && cached is not null)
        {
            return cached;
        }

        var bucketExpression = BucketByInterval[normalizedInterval];

        await using var connection = await OpenConnectionAsync(cancellationToken);

        var candleSql = BuildCandlesSql(bucketExpression, normalizedInterval == "1m");

        var candles = (await connection.QueryAsync<CandlePoint>(new CommandDefinition(
            candleSql,
            new
            {
                Symbol = symbol,
                Interval = "1m",
                StartUtc = startUtc,
                EndUtc = endUtc,
                MaxRows = _options.MaxCandlesPerRequest
            },
            cancellationToken: cancellationToken))).ToList();

        var comparisonSql = BuildComparisonSql(bucketExpression);

        var comparison = (await connection.QueryAsync<SeriesPoint>(new CommandDefinition(
            comparisonSql,
            new
            {
                Symbols = comparisonSymbols,
                Interval = "1m",
                StartUtc = startUtc,
                EndUtc = endUtc,
                MaxRows = _options.MaxCandlesPerRequest
            },
            cancellationToken: cancellationToken))).ToList();

        var result = new CandlesResponse(symbol, normalizedInterval, candles, comparison);
        _cache.Set(cacheKey, result, TimeSpan.FromSeconds(Math.Max(5, _options.CacheSeconds / 2)));
        return result;
    }

    public async Task<QualityResponse> GetQualityAsync(
        DateTime startUtc,
        DateTime endUtc,
        string[] symbols,
        string interval,
        CancellationToken cancellationToken)
    {
        var normalizedInterval = NormalizeInterval(interval);
        var cacheKey = BuildCacheKey("quality", startUtc, endUtc, normalizedInterval, symbols, 0, 0, null);

        if (_cache.TryGetValue(cacheKey, out QualityResponse? cached) && cached is not null)
        {
            return cached;
        }

        var expectedPerDay = ExpectedCandlesPerDay(normalizedInterval);

        await using var connection = await OpenConnectionAsync(cancellationToken);

        const string countsSql = """
            SELECT symbol AS Symbol,
                   date_trunc('day', time)::date AS Day,
                   COUNT(*)::int AS Candles
            FROM historical_collector.price_ticks
            WHERE interval = @Interval
              AND symbol = ANY(@Symbols)
              AND time >= @StartUtc
              AND time < @EndUtc
            GROUP BY symbol, date_trunc('day', time)::date
            ORDER BY symbol, Day;
            """;

        var counts = (await connection.QueryAsync<DailyRawRow>(new CommandDefinition(
            countsSql,
            new { Interval = normalizedInterval, Symbols = symbols, StartUtc = startUtc, EndUtc = endUtc },
            cancellationToken: cancellationToken))).ToList();

        var countLookup = counts
            .GroupBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToDictionary(item => item.Day, item => item.Candles), StringComparer.OrdinalIgnoreCase);

        var dailyRows = new List<DailyCoverageRow>();
        for (var day = DateOnly.FromDateTime(startUtc.Date); day < DateOnly.FromDateTime(endUtc.Date); day = day.AddDays(1))
        {
            foreach (var symbol in symbols)
            {
                var actual = countLookup.TryGetValue(symbol, out var values) && values.TryGetValue(day, out var count)
                    ? count
                    : 0;
                var missing = Math.Max(0, expectedPerDay - actual);

                dailyRows.Add(new DailyCoverageRow(day, symbol, expectedPerDay, actual, missing));
            }
        }

        var gapPage = await GetGapsAsync(startUtc, endUtc, symbols, normalizedInterval, 1, _options.DefaultPageSize, cancellationToken);
        var histogram = BuildDurationHistogram(gapPage.Rows);

        var result = new QualityResponse(startUtc, endUtc, normalizedInterval, dailyRows, gapPage.Rows, histogram);
        _cache.Set(cacheKey, result, TimeSpan.FromSeconds(_options.CacheSeconds));
        return result;
    }

    public async Task<PagedResponse<GapRow>> GetGapsAsync(
        DateTime startUtc,
        DateTime endUtc,
        string[] symbols,
        string interval,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedInterval = NormalizeInterval(interval);
        var normalizedPageNumber = NormalizePageNumber(pageNumber);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var offset = (normalizedPageNumber - 1) * normalizedPageSize;

        var cacheKey = BuildCacheKey("gaps", startUtc, endUtc, normalizedInterval, symbols, normalizedPageNumber, normalizedPageSize, null);
        if (_cache.TryGetValue(cacheKey, out PagedResponse<GapRow>? cached) && cached is not null)
        {
            return cached;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);

        const string countSql = """
            SELECT COUNT(*)::int
            FROM historical_collector.data_gaps
            WHERE interval = @Interval
              AND symbol = ANY(@Symbols)
              AND gap_start < @EndUtc
              AND gap_end >= @StartUtc;
            """;

        var totalRows = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            countSql,
            new { Interval = normalizedInterval, Symbols = symbols, StartUtc = startUtc, EndUtc = endUtc },
            cancellationToken: cancellationToken));

        const string gapsSql = """
            SELECT id AS Id,
                   symbol AS Symbol,
                   interval AS Interval,
                   gap_start AS GapStart,
                   gap_end AS GapEnd,
                   detected_at AS DetectedAt,
                   filled_at AS FilledAt
            FROM historical_collector.data_gaps
            WHERE interval = @Interval
              AND symbol = ANY(@Symbols)
              AND gap_start < @EndUtc
              AND gap_end >= @StartUtc
            ORDER BY gap_start DESC
            LIMIT @PageSize OFFSET @Offset;
            """;

        var rawRows = await connection.QueryAsync<GapRawRow>(new CommandDefinition(
            gapsSql,
            new
            {
                Interval = normalizedInterval,
                Symbols = symbols,
                StartUtc = startUtc,
                EndUtc = endUtc,
                PageSize = normalizedPageSize,
                Offset = offset
            },
            cancellationToken: cancellationToken));

        var rows = rawRows
            .Select(static row =>
            {
                var duration = Math.Max(0d, (row.GapEnd - row.GapStart).TotalMinutes);
                var fillLatency = row.FilledAt is null ? null : (double?)Math.Max(0d, (row.FilledAt.Value - row.DetectedAt).TotalMinutes);
                return new GapRow(
                    row.Id,
                    row.Symbol,
                    row.Interval,
                    row.GapStart,
                    row.GapEnd,
                    row.DetectedAt,
                    row.FilledAt,
                    Math.Round(duration, 2),
                    fillLatency is null ? null : Math.Round(fillLatency.Value, 2));
            })
            .ToList();

        var result = new PagedResponse<GapRow>(normalizedPageNumber, normalizedPageSize, totalRows, rows);
        _cache.Set(cacheKey, result, TimeSpan.FromSeconds(_options.CacheSeconds));
        return result;
    }

    public async Task<SchemaResponse> GetSchemaAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "schema::historical_collector";
        if (_cache.TryGetValue(cacheKey, out SchemaResponse? cached) && cached is not null)
        {
            return cached;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);

        const string tablesSql = """
            SELECT table_schema AS SchemaName,
                   table_name AS TableName,
                   table_type AS TableType
            FROM information_schema.tables
            WHERE table_schema = 'historical_collector'
              AND (table_name LIKE 'price_%_ticks' OR table_name = 'price_ticks' OR table_name = 'data_gaps')
            ORDER BY table_name;
            """;

        const string columnsSql = """
            SELECT table_schema AS SchemaName,
                   table_name AS TableName,
                   column_name AS ColumnName,
                   data_type AS DataType,
                   is_nullable = 'YES' AS IsNullable,
                   ordinal_position AS OrdinalPosition
            FROM information_schema.columns
            WHERE table_schema = 'historical_collector'
              AND (table_name LIKE 'price_%_ticks' OR table_name = 'price_ticks' OR table_name = 'data_gaps')
            ORDER BY table_name, ordinal_position;
            """;

        const string constraintsSql = """
            SELECT tc.table_name AS TableName,
                   tc.constraint_name AS ConstraintName,
                   tc.constraint_type AS ConstraintType,
                   COALESCE(kcu.column_name, '') AS ColumnName
            FROM information_schema.table_constraints tc
            LEFT JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_schema = kcu.table_schema
             AND tc.table_name = kcu.table_name
            WHERE tc.table_schema = 'historical_collector'
              AND (tc.table_name LIKE 'price_%_ticks' OR tc.table_name = 'data_gaps')
            ORDER BY tc.table_name, tc.constraint_type, tc.constraint_name;
            """;

        const string indexesSql = """
            SELECT schemaname AS SchemaName,
                   tablename AS TableName,
                   indexname AS IndexName,
                   indexdef AS IndexDefinition
            FROM pg_indexes
            WHERE schemaname = 'historical_collector'
              AND (tablename LIKE 'price_%_ticks' OR tablename = 'data_gaps')
            ORDER BY tablename, indexname;
            """;

        var tables = (await connection.QueryAsync<TableInfo>(new CommandDefinition(tablesSql, cancellationToken: cancellationToken))).ToList();
        var columns = (await connection.QueryAsync<ColumnInfo>(new CommandDefinition(columnsSql, cancellationToken: cancellationToken))).ToList();
        var constraints = (await connection.QueryAsync<ConstraintInfo>(new CommandDefinition(constraintsSql, cancellationToken: cancellationToken))).ToList();
        var indexes = (await connection.QueryAsync<IndexInfo>(new CommandDefinition(indexesSql, cancellationToken: cancellationToken))).ToList();

        var result = new SchemaResponse(tables, columns, constraints, indexes);
        _cache.Set(cacheKey, result, TimeSpan.FromSeconds(Math.Max(30, _options.CacheSeconds * 3)));
        return result;
    }

    public async Task<WorkbenchResponse> RunWorkbenchTemplateAsync(
        string templateId,
        DateTime startUtc,
        DateTime endUtc,
        string[] symbols,
        string interval,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedInterval = NormalizeInterval(interval);
        var normalizedPageNumber = NormalizePageNumber(pageNumber);
        var normalizedPageSize = NormalizePageSize(pageSize);

        var cacheKey = BuildCacheKey("workbench", startUtc, endUtc, normalizedInterval, symbols, normalizedPageNumber, normalizedPageSize, templateId);
        if (_cache.TryGetValue(cacheKey, out WorkbenchResponse? cached) && cached is not null)
        {
            return cached;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);

        var (sql, parameters) = templateId.Trim().ToLowerInvariant() switch
        {
            "volatile-days" =>
                (
                    """
                    SELECT symbol,
                           date_trunc('day', time)::date AS day,
                           ROUND(((MAX(high) - MIN(low)) / NULLIF(MIN(low), 0))::numeric * 100, 4) AS range_pct,
                           COUNT(*)::int AS candle_count
                    FROM historical_collector.price_ticks
                    WHERE interval = @Interval
                      AND symbol = ANY(@Symbols)
                      AND time >= @StartUtc
                      AND time < @EndUtc
                    GROUP BY symbol, date_trunc('day', time)::date
                    ORDER BY range_pct DESC NULLS LAST
                    LIMIT 200;
                    """,
                    new { Interval = normalizedInterval, Symbols = symbols, StartUtc = startUtc, EndUtc = endUtc } as object
                ),
            "monthly-volume" =>
                (
                    """
                    SELECT symbol,
                           date_trunc('month', time)::date AS month,
                           ROUND(SUM(volume)::numeric, 8) AS total_volume,
                           COUNT(*)::int AS candle_count
                    FROM historical_collector.price_ticks
                    WHERE interval = @Interval
                      AND symbol = ANY(@Symbols)
                      AND time >= @StartUtc
                      AND time < @EndUtc
                    GROUP BY symbol, date_trunc('month', time)::date
                    ORDER BY month DESC, symbol;
                    """,
                    new { Interval = normalizedInterval, Symbols = symbols, StartUtc = startUtc, EndUtc = endUtc } as object
                ),
            "missing-data-report" =>
                (
                    """
                    WITH days AS (
                        SELECT generate_series(date_trunc('day', @StartUtc::timestamptz), date_trunc('day', @EndUtc::timestamptz - interval '1 day'), interval '1 day')::date AS day
                    ),
                    expected AS (
                        SELECT s.symbol, d.day
                        FROM unnest(@Symbols::text[]) AS s(symbol)
                        CROSS JOIN days d
                    ),
                    actual AS (
                        SELECT symbol,
                               date_trunc('day', time)::date AS day,
                               COUNT(*)::int AS candles
                        FROM historical_collector.price_ticks
                        WHERE interval = @Interval
                          AND symbol = ANY(@Symbols)
                          AND time >= @StartUtc
                          AND time < @EndUtc
                        GROUP BY symbol, date_trunc('day', time)::date
                    )
                    SELECT e.symbol,
                           e.day,
                           COALESCE(a.candles, 0) AS actual_candles,
                           GREATEST(@ExpectedPerDay - COALESCE(a.candles, 0), 0) AS missing_candles
                    FROM expected e
                    LEFT JOIN actual a ON a.symbol = e.symbol AND a.day = e.day
                    ORDER BY e.day DESC, e.symbol;
                    """,
                    new
                    {
                        Interval = normalizedInterval,
                        Symbols = symbols,
                        StartUtc = startUtc,
                        EndUtc = endUtc,
                        ExpectedPerDay = ExpectedCandlesPerDay(normalizedInterval)
                    } as object
                ),
            _ => throw new InvalidOperationException($"Unknown template '{templateId}'.")
        };

        var rows = await connection.QueryAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var mappedRows = ConvertRows(rows).ToList();
        var totalRows = mappedRows.Count;
        var pagedRows = mappedRows
            .Skip((normalizedPageNumber - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToList();

        var columns = pagedRows.Count == 0
            ? Array.Empty<string>()
            : pagedRows[0].Keys.ToArray();

        var result = new WorkbenchResponse(templateId, normalizedPageNumber, normalizedPageSize, totalRows, columns, pagedRows);
        _cache.Set(cacheKey, result, TimeSpan.FromSeconds(_options.CacheSeconds));
        return result;
    }

    private static IEnumerable<Dictionary<string, object?>> ConvertRows(IEnumerable<dynamic> rows)
    {
        foreach (var row in rows)
        {
            if (row is not IDictionary<string, object?> dictionary)
            {
                continue;
            }

            var mapped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in dictionary)
            {
                mapped[key] = value switch
                {
                    DateTime dateTime => dateTime.ToUniversalTime(),
                    DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
                    _ => value
                };
            }

            yield return mapped;
        }
    }

    private static string BuildCandlesSql(string bucketExpression, bool isRaw)
    {
        if (isRaw)
        {
            return """
                SELECT time AS Time,
                       open AS Open,
                       high AS High,
                       low AS Low,
                       close AS Close,
                       volume AS Volume
                FROM historical_collector.price_ticks
                WHERE symbol = @Symbol
                  AND interval = @Interval
                  AND time >= @StartUtc
                  AND time < @EndUtc
                ORDER BY time
                LIMIT @MaxRows;
                """;
        }

        return $"""
            WITH grouped AS (
                SELECT {bucketExpression} AS bucket,
                       time,
                       open,
                       high,
                       low,
                       close,
                       volume
                FROM historical_collector.price_ticks
                WHERE symbol = @Symbol
                  AND interval = @Interval
                  AND time >= @StartUtc
                  AND time < @EndUtc
            )
            SELECT bucket AS Time,
                   (array_agg(open ORDER BY time))[1] AS Open,
                   MAX(high) AS High,
                   MIN(low) AS Low,
                   (array_agg(close ORDER BY time DESC))[1] AS Close,
                   SUM(volume) AS Volume
            FROM grouped
            GROUP BY bucket
            ORDER BY bucket
            LIMIT @MaxRows;
            """;
    }

    private static string BuildComparisonSql(string bucketExpression)
    {
        return $"""
            WITH grouped AS (
                SELECT {bucketExpression} AS bucket,
                       symbol,
                       time,
                       close
                FROM historical_collector.price_ticks
                WHERE symbol = ANY(@Symbols)
                  AND interval = @Interval
                  AND time >= @StartUtc
                  AND time < @EndUtc
            )
            SELECT bucket AS Time,
                   symbol AS Symbol,
                   (array_agg(close ORDER BY time DESC))[1] AS Close
            FROM grouped
            GROUP BY bucket, symbol
            ORDER BY bucket, symbol
            LIMIT @MaxRows;
            """;
    }

    private static IReadOnlyList<HistogramBucket> BuildDurationHistogram(IReadOnlyList<GapRow> gaps)
    {
        var buckets = new[]
        {
            new HistogramBucket("0-5m", 0),
            new HistogramBucket("5-15m", 0),
            new HistogramBucket("15-60m", 0),
            new HistogramBucket("1-4h", 0),
            new HistogramBucket(">4h", 0)
        };

        var counts = buckets.ToDictionary(item => item.Label, _ => 0);

        foreach (var gap in gaps)
        {
            var minutes = gap.DurationMinutes;
            var label = minutes switch
            {
                <= 5 => "0-5m",
                <= 15 => "5-15m",
                <= 60 => "15-60m",
                <= 240 => "1-4h",
                _ => ">4h"
            };

            counts[label]++;
        }

        return buckets
            .Select(item => item with { Count = counts[item.Label] })
            .ToList();
    }

    private static int ExpectedCandlesPerDay(string interval) => interval.ToLowerInvariant() switch
    {
        "1m" => 1440,
        "5m" => 288,
        "15m" => 96,
        "1h" => 24,
        "1d" => 1,
        _ => 1440
    };

    private string NormalizeInterval(string interval)
    {
        var candidate = string.IsNullOrWhiteSpace(interval) ? "1m" : interval.Trim();
        if (_options.AllowedIntervals.Any(value => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            return candidate.ToLowerInvariant();
        }

        return "1m";
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private int NormalizePageNumber(int pageNumber) => pageNumber <= 0 ? 1 : pageNumber;

    private int NormalizePageSize(int pageSize)
    {
        if (pageSize <= 0)
        {
            return _options.DefaultPageSize;
        }

        return Math.Min(pageSize, _options.MaxPageSize);
    }

    private static string BuildCacheKey(
        string prefix,
        DateTime startUtc,
        DateTime endUtc,
        string interval,
        IEnumerable<string> symbols,
        int pageNumber,
        int pageSize,
        string? extra)
    {
        var normalizedSymbols = string.Join(",", symbols.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
        return $"{prefix}|{startUtc:O}|{endUtc:O}|{interval}|{normalizedSymbols}|{pageNumber}|{pageSize}|{extra}";
    }

    public async Task<IReadOnlyList<OrderRow>> GetRecentOrdersAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT id            AS OrderId,
                   symbol        AS Symbol,
                   side          AS Side,
                   order_type    AS OrderType,
                   quantity      AS Quantity,
                   price         AS EntryPrice,
                   stop_loss     AS StopLoss,
                   take_profit   AS TakeProfit,
                   filled_price  AS FilledPrice,
                   filled_qty    AS FilledQty,
                   success       AS Success,
                   is_paper      AS IsPaperTrade,
                   error_msg     AS ErrorMessage,
                   time          AS CreatedAt
            FROM public.orders
            ORDER BY time DESC
            LIMIT @Limit;
            """;

        var rows = await connection.QueryAsync<OrderRow>(new CommandDefinition(
            sql,
            new { Limit = Math.Clamp(limit, 1, 100) },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    private sealed record OverviewRawRow(string Symbol, long RowCount, DateTime? LatestTimeUtc);

    private sealed record DailyRawRow(string Symbol, DateOnly Day, int Candles);

    private sealed record GapRawRow(
        long Id,
        string Symbol,
        string Interval,
        DateTime GapStart,
        DateTime GapEnd,
        DateTime DetectedAt,
        DateTime? FilledAt);

    public async Task<PriceSummary?> GetPriceSummaryAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                (SELECT open FROM historical_collector.price_ticks
                 WHERE symbol = @Symbol AND interval = '1m' AND time >= @From AND time < @To
                 ORDER BY time ASC LIMIT 1)  AS OpenPrice,
                MAX(high)                    AS HighPrice,
                MIN(low)                     AS LowPrice,
                (SELECT close FROM historical_collector.price_ticks
                 WHERE symbol = @Symbol AND interval = '1m' AND time >= @From AND time < @To
                 ORDER BY time DESC LIMIT 1) AS ClosePrice,
                COUNT(*)                     AS TotalTicks,
                MIN(time)                    AS FirstTickUtc,
                MAX(time)                    AS LastTickUtc
            FROM historical_collector.price_ticks
            WHERE symbol = @Symbol AND interval = '1m' AND time >= @From AND time < @To;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<PriceSummaryRaw>(
            new CommandDefinition(sql, new { Symbol = symbol, From = from, To = to }, cancellationToken: cancellationToken));

        if (row is null || row.TotalTicks == 0)
            return null;

        return new PriceSummary(
            row.OpenPrice,
            row.HighPrice,
            row.LowPrice,
            row.ClosePrice,
            row.TotalTicks,
            row.FirstTickUtc,
            row.LastTickUtc);
    }

    private sealed record PriceSummaryRaw(
        decimal? OpenPrice,
        decimal? HighPrice,
        decimal? LowPrice,
        decimal? ClosePrice,
        long TotalTicks,
        DateTime? FirstTickUtc,
        DateTime? LastTickUtc);
}