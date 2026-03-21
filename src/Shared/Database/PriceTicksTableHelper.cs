using CryptoTrading.Shared.DTOs;
using Npgsql;

namespace CryptoTrading.Shared.Database;

/// <summary>
/// Helper class for managing yearly partitioned price_ticks tables.
/// Automatically routes inserts to the correct table based on timestamp.
/// </summary>
public static class PriceTicksTableHelper
{
    private const string SchemaName = "historical_collector";

    /// <summary>
    /// Gets the fully qualified table name for a given timestamp.
    /// Example: "historical_collector.price_2025_ticks"
    /// </summary>
    public static string GetTableName(DateTime timestamp)
    {
        var year = timestamp.Year;
        return $"{SchemaName}.price_{year}_ticks";
    }

    /// <summary>
    /// Gets the fully qualified table name for a given year.
    /// Example: "historical_collector.price_2025_ticks"
    /// </summary>
    public static string GetTableNameForYear(int year)
    {
        return $"{SchemaName}.price_{year}_ticks";
    }

    /// <summary>
    /// Ensures the table exists for the given timestamp.
    /// Creates the table if it doesn't exist.
    /// </summary>
    public static async Task<string> EnsureTableExistsAsync(
        NpgsqlConnection connection,
        DateTime timestamp,
        CancellationToken cancellationToken = default)
    {
        var sql = "SELECT historical_collector.ensure_price_ticks_table_exists(@Timestamp);";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("Timestamp", timestamp);

        var tableName = await cmd.ExecuteScalarAsync(cancellationToken);
        return tableName?.ToString() ?? GetTableName(timestamp);
    }

    /// <summary>
    /// Groups price ticks by year for efficient batch insertion into yearly tables.
    /// </summary>
    public static Dictionary<int, List<PriceTick>> GroupByYear(IEnumerable<PriceTick> ticks)
    {
        var grouped = new Dictionary<int, List<PriceTick>>();

        foreach (var tick in ticks)
        {
            var year = tick.Timestamp.Year;

            if (!grouped.TryGetValue(year, out var list))
            {
                list = new List<PriceTick>();
                grouped[year] = list;
            }

            list.Add(tick);
        }

        return grouped;
    }

    /// <summary>
    /// Generates INSERT statement for a specific yearly table.
    /// </summary>
    public static string GenerateInsertSql(int year)
    {
        var tableName = GetTableNameForYear(year);
        return $"""
            INSERT INTO {tableName} (time, symbol, price, volume, open, high, low, close, interval)
            VALUES (@Timestamp, @Symbol, @Price, @Volume, @Open, @High, @Low, @Close, @Interval)
            ON CONFLICT (time, symbol, interval) DO NOTHING;
            """;
    }

    /// <summary>
    /// Generates a query that spans multiple years using UNION ALL.
    /// </summary>
    public static string GenerateUnionQuery(int startYear, int endYear, string whereClause = "")
    {
        var queries = new List<string>();

        for (var year = startYear; year <= endYear; year++)
        {
            var tableName = GetTableNameForYear(year);
            var query = $"SELECT time, symbol, price, volume, open, high, low, close, interval FROM {tableName}";

            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                query += $" WHERE {whereClause}";
            }

            queries.Add(query);
        }

        return string.Join(" UNION ALL ", queries);
    }

    /// <summary>
    /// Gets the schema name used for historical data.
    /// </summary>
    public static string GetSchemaName() => SchemaName;

    /// <summary>
    /// Gets the data_gaps table name with schema.
    /// </summary>
    public static string GetDataGapsTableName() => $"{SchemaName}.data_gaps";
}
