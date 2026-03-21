using System.Diagnostics;
using Dapper;
using HouseKeeper.Worker.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HouseKeeper.Worker.Jobs;

public sealed class PriceTicksPartitionJob(
    IOptions<HouseKeeperSettings> settings,
    ILogger<PriceTicksPartitionJob> logger,
    string connectionString) : ICleanupJob
{
    public string Name => "PriceTicksPartitionAudit";

    public async Task<JobResult> RunAsync(bool dryRun, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var retentionMonths = settings.Value.Retention.PriceTicksMonths;
        var cutoffYear = DateTime.UtcNow.AddMonths(-retentionMonths).Year;

        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            // Find yearly partition tables older than retention threshold
            var discoverSql = """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'historical_collector'
                  AND table_name LIKE 'price_%_ticks'
                  AND table_type = 'BASE TABLE'
                ORDER BY table_name
                """;

            var tables = (await conn.QueryAsync<string>(discoverSql)).ToList();

            var oldPartitions = tables
                .Where(t => TryExtractYear(t, out int yr) && yr < cutoffYear)
                .ToList();

            if (oldPartitions.Count == 0)
            {
                var msg = $"No price_ticks partitions older than {retentionMonths} months (cutoff year {cutoffYear}). Nothing to do.";
                logger.LogInformation("{Summary}", msg);
                return new JobResult { JobName = Name, DryRun = dryRun, RowsAffected = 0, Summary = msg, Duration = sw.Elapsed };
            }

            foreach (var table in oldPartitions)
            {
                var rowCount = await conn.ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM historical_collector.\"{table}\"");

                if (dryRun)
                    logger.LogInformation("[DRY-RUN] Partition historical_collector.{Table} has {Rows} rows and is older than retention. Would drop.", table, rowCount);
                else
                    logger.LogWarning("Partition historical_collector.{Table} has {Rows} rows and exceeds {Months}-month retention. Manual review required before drop.", table, rowCount, retentionMonths);
            }

            var summary = dryRun
                ? $"[DRY-RUN] {oldPartitions.Count} partitions exceed {retentionMonths}-month retention: {string.Join(", ", oldPartitions)}."
                : $"AUDIT: {oldPartitions.Count} partitions exceed retention. Manual drop required: {string.Join(", ", oldPartitions)}.";

            logger.LogInformation("{Summary}", summary);
            return new JobResult { JobName = Name, DryRun = dryRun, RowsAffected = oldPartitions.Count, Summary = summary, Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PriceTicksPartitionJob failed");
            return new JobResult { JobName = Name, DryRun = dryRun, Error = ex.Message, Summary = "Failed.", Duration = sw.Elapsed };
        }
    }

    private static bool TryExtractYear(string tableName, out int year)
    {
        // pattern: price_YYYY_ticks
        var parts = tableName.Split('_');
        if (parts.Length >= 3 && int.TryParse(parts[1], out year))
            return true;
        year = 0;
        return false;
    }
}
