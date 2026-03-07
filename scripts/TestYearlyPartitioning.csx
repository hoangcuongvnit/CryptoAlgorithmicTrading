using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Database;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

Console.WriteLine("=== Testing Yearly Partitioned Tables ===\n");

// Build configuration
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5433;Database=cryptotrading;Username=postgres;Password=postgres"
    })
    .Build();

var connectionString = configuration.GetConnectionString("Postgres")!;

// Test data spanning two years
var testTicks = new List<PriceTick>
{
    // 2025 data
    new PriceTick
    {
        Timestamp = new DateTime(2025, 12, 31, 23, 59, 0, DateTimeKind.Utc),
        Symbol = "TESTUSDT",
        Price = 100.5m,
        Volume = 1000m,
        Open = 100m,
        High = 101m,
        Low = 99m,
        Close = 100.5m,
        Interval = "1m"
    },
    // 2026 data
    new PriceTick
    {
        Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Symbol = "TESTUSDT",
        Price = 101.5m,
        Volume = 1100m,
        Open = 101m,
        High = 102m,
        Low = 100m,
        Close = 101.5m,
        Interval = "1m"
    },
    new PriceTick
    {
        Timestamp = new DateTime(2026, 3, 7, 12, 30, 0, DateTimeKind.Utc),
        Symbol = "TESTUSDT",
        Price = 102.5m,
        Volume = 1200m,
        Open = 102m,
        High = 103m,
        Low = 101m,
        Close = 102.5m,
        Interval = "1m"
    }
};

Console.WriteLine($"Inserting {testTicks.Count} test ticks:");
foreach (var tick in testTicks)
{
    Console.WriteLine($"  - {tick.Timestamp:yyyy-MM-dd HH:mm:ss} UTC | {tick.Symbol} | ${tick.Price}");
}
Console.WriteLine();

// Test insertion
await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

// Group by year
var groupedByYear = PriceTicksTableHelper.GroupByYear(testTicks);
Console.WriteLine($"Grouped into {groupedByYear.Count} yearly batches:");
foreach (var (year, ticks) in groupedByYear)
{
    Console.WriteLine($"  - Year {year}: {ticks.Count} ticks");
}
Console.WriteLine();

// Insert data
var totalInserted = 0;
foreach (var (year, yearTicks) in groupedByYear)
{
    Console.WriteLine($"Processing year {year}...");
    
    // Ensure table exists
    var tableName = await PriceTicksTableHelper.EnsureTableExistsAsync(
        connection, yearTicks[0].Timestamp, CancellationToken.None);
    Console.WriteLine($"  Table: {tableName}");
    
    // Generate INSERT SQL
    var insertSql = PriceTicksTableHelper.GenerateInsertSql(year);
    Console.WriteLine($"  SQL: {insertSql.Substring(0, Math.Min(80, insertSql.Length))}...");
    
    // Execute insert
    var inserted = await connection.ExecuteAsync(insertSql, yearTicks);
    totalInserted += inserted;
    Console.WriteLine($"  ✓ Inserted {inserted} rows");
    Console.WriteLine();
}

Console.WriteLine($"Total inserted: {totalInserted} rows\n");

// Verify data
Console.WriteLine("=== Verification ===\n");

// Check 2025 table
var count2025 = await connection.ExecuteScalarAsync<int>(
    "SELECT COUNT(*) FROM historical_collector.price_2025_ticks WHERE symbol = 'TESTUSDT'");
Console.WriteLine($"✓ price_2025_ticks: {count2025} rows for TESTUSDT");

// Check 2026 table
var count2026 = await connection.ExecuteScalarAsync<int>(
    "SELECT COUNT(*) FROM historical_collector.price_2026_ticks WHERE symbol = 'TESTUSDT'");
Console.WriteLine($"✓ price_2026_ticks: {count2026} rows for TESTUSDT");

// Query via unified view
var countView = await connection.ExecuteScalarAsync<int>(
    "SELECT COUNT(*) FROM historical_collector.price_ticks WHERE symbol = 'TESTUSDT'");
Console.WriteLine($"✓ Unified view: {countView} rows for TESTUSDT");

// Get the data back
var results = await connection.QueryAsync<dynamic>(
    "SELECT time, symbol, price, close FROM historical_collector.price_ticks WHERE symbol = 'TESTUSDT' ORDER BY time");

Console.WriteLine("\nRetrieved data:");
foreach (var row in results)
{
    Console.WriteLine($"  {row.time:yyyy-MM-dd HH:mm:ss} | {row.symbol} | ${row.price} | Close: ${row.close}");
}

// Test ON CONFLICT (insert duplicate)
Console.WriteLine("\n=== Testing Duplicate Prevention ===\n");
var duplicateInserted = 0;
foreach (var (year, yearTicks) in groupedByYear)
{
    var insertSql = PriceTicksTableHelper.GenerateInsertSql(year);
    var inserted = await connection.ExecuteAsync(insertSql, yearTicks);
    duplicateInserted += inserted;
}
Console.WriteLine($"Attempting to insert duplicates: {duplicateInserted} rows inserted (should be 0)");

// Final count
var finalCount = await connection.ExecuteScalarAsync<int>(
    "SELECT COUNT(*) FROM historical_collector.price_ticks WHERE symbol = 'TESTUSDT'");
Console.WriteLine($"Final count: {finalCount} rows (should stay {countView})");

Console.WriteLine("\n=== Test Completed Successfully! ===");
