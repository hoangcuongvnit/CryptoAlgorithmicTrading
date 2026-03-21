using System.Data;
using System.Diagnostics;
using Dapper;
using HouseKeeper.Worker.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HouseKeeper.Worker.Jobs;

public sealed class DataGapsCleanupJob(
    IOptions<HouseKeeperSettings> settings,
    ILogger<DataGapsCleanupJob> logger,
    string connectionString) : ICleanupJob
{
    public string Name => "DataGapsCleanup";

    public async Task<JobResult> RunAsync(bool dryRun, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var retention = settings.Value.Retention;
        var batchSize = settings.Value.BatchSize;

        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            var countSql = """
                SELECT COUNT(*) FROM historical_collector.data_gaps
                WHERE filled_at IS NOT NULL
                  AND filled_at < NOW() - INTERVAL '1 day' * @days
                """;

            var eligible = await conn.ExecuteScalarAsync<long>(
                countSql, new { days = retention.FilledGapsDays });

            if (dryRun)
            {
                var summary = $"[DRY-RUN] Would delete {eligible} filled data_gaps rows older than {retention.FilledGapsDays} days.";
                logger.LogInformation("{Summary}", summary);
                return new JobResult { JobName = Name, DryRun = true, RowsAffected = eligible, Summary = summary, Duration = sw.Elapsed };
            }

            var deleteSql = """
                DELETE FROM historical_collector.data_gaps
                WHERE id IN (
                    SELECT id FROM historical_collector.data_gaps
                    WHERE filled_at IS NOT NULL
                      AND filled_at < NOW() - INTERVAL '1 day' * @days
                    LIMIT @batch
                )
                """;

            long totalDeleted = 0;
            int deleted;
            do
            {
                deleted = await conn.ExecuteAsync(deleteSql, new { days = retention.FilledGapsDays, batch = batchSize });
                totalDeleted += deleted;
                logger.LogDebug("DataGapsCleanup: deleted batch of {Count} rows (total={Total})", deleted, totalDeleted);
            } while (deleted == batchSize && !ct.IsCancellationRequested);

            var msg = $"Deleted {totalDeleted} filled data_gaps rows older than {retention.FilledGapsDays} days.";
            logger.LogInformation("{Summary}", msg);
            return new JobResult { JobName = Name, DryRun = false, RowsAffected = totalDeleted, Summary = msg, Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DataGapsCleanupJob failed");
            return new JobResult { JobName = Name, DryRun = dryRun, Error = ex.Message, Summary = "Failed.", Duration = sw.Elapsed };
        }
    }
}
