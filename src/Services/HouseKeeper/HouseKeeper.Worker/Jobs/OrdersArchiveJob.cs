using Dapper;
using HouseKeeper.Worker.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Diagnostics;

namespace HouseKeeper.Worker.Jobs;

public sealed class OrdersArchiveJob(
    IOptions<HouseKeeperSettings> settings,
    ILogger<OrdersArchiveJob> logger,
    string connectionString) : ICleanupJob
{
    public string Name => "OrdersArchive";

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
                SELECT COUNT(*) FROM public.orders
                WHERE status IN ('CLOSED', 'FAILED')
                  AND time < NOW() - INTERVAL '1 day' * @days
                """;

            var eligible = await conn.ExecuteScalarAsync<long>(
                countSql, new { days = retention.OrdersDays });

            if (dryRun)
            {
                var summary = $"[DRY-RUN] Would archive {eligible} CLOSED/FAILED orders older than {retention.OrdersDays} days.";
                logger.LogInformation("{Summary}", summary);
                return new JobResult { JobName = Name, DryRun = true, RowsAffected = eligible, Summary = summary, Duration = sw.Elapsed };
            }

            long totalArchived = 0;
            int archived;
            do
            {
                // Archive + delete in a single transaction per batch
                await using var tx = await conn.BeginTransactionAsync(ct);

                var archiveSql = """
                    WITH batch AS (
                        SELECT id FROM public.orders
                        WHERE status IN ('CLOSED', 'FAILED')
                          AND time < NOW() - INTERVAL '1 day' * @days
                        LIMIT @batch
                        FOR UPDATE SKIP LOCKED
                    )
                    INSERT INTO archive.orders_history
                    SELECT o.* FROM public.orders o
                    INNER JOIN batch b ON b.id = o.id
                    ON CONFLICT (id) DO NOTHING
                    """;

                var deleteSql = """
                    WITH batch AS (
                        SELECT id FROM public.orders
                        WHERE status IN ('CLOSED', 'FAILED')
                          AND time < NOW() - INTERVAL '1 day' * @days
                        LIMIT @batch
                        FOR UPDATE SKIP LOCKED
                    )
                    DELETE FROM public.orders
                    WHERE id IN (SELECT id FROM batch)
                    """;

                await conn.ExecuteAsync(archiveSql, new { days = retention.OrdersDays, batch = batchSize }, tx);
                archived = await conn.ExecuteAsync(deleteSql, new { days = retention.OrdersDays, batch = batchSize }, tx);
                await tx.CommitAsync(ct);

                totalArchived += archived;
                logger.LogDebug("OrdersArchive: archived batch of {Count} rows (total={Total})", archived, totalArchived);
            } while (archived == batchSize && !ct.IsCancellationRequested);

            var msg = $"Archived {totalArchived} CLOSED/FAILED orders older than {retention.OrdersDays} days.";
            logger.LogInformation("{Summary}", msg);
            return new JobResult { JobName = Name, DryRun = false, RowsAffected = totalArchived, Summary = msg, Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OrdersArchiveJob failed");
            return new JobResult { JobName = Name, DryRun = dryRun, Error = ex.Message, Summary = "Failed.", Duration = sw.Elapsed };
        }
    }
}
