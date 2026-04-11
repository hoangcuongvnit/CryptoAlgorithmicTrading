using Dapper;
using Npgsql;
using System.Diagnostics;

namespace HouseKeeper.Worker.Jobs;

public sealed class UnusedTableAuditJob(
    ILogger<UnusedTableAuditJob> logger,
    string connectionString) : ICleanupJob
{
    private static readonly string[] CandidateTables = ["active_symbols", "account_balance", "trade_signals"];

    public string Name => "UnusedTableAudit";

    public async Task<JobResult> RunAsync(bool dryRun, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            var sql = """
                SELECT
                    schemaname,
                    relname                AS table_name,
                    n_live_tup             AS estimated_rows,
                    last_vacuum,
                    last_autovacuum,
                    last_analyze,
                    last_autoanalyze
                FROM pg_stat_user_tables
                WHERE relname = ANY(@tables)
                ORDER BY relname
                """;

            var rows = await conn.QueryAsync(sql, new { tables = CandidateTables });

            var lines = new List<string>();
            foreach (var row in rows)
            {
                var line = $"  {row.schemaname}.{row.table_name}: ~{row.estimated_rows} rows, " +
                           $"last_analyze={row.last_analyze?.ToString("yyyy-MM-dd") ?? "never"}, " +
                           $"last_vacuum={row.last_vacuum?.ToString("yyyy-MM-dd") ?? "never"}";
                lines.Add(line);
                logger.LogInformation("UnusedTableAudit | {Line}", line);
            }

            var summary = $"[AUDIT-ONLY] Candidate unused tables:\n{string.Join("\n", lines)}";
            return new JobResult { JobName = Name, DryRun = dryRun, RowsAffected = lines.Count, Summary = summary, Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UnusedTableAuditJob failed");
            return new JobResult { JobName = Name, DryRun = dryRun, Error = ex.Message, Summary = "Failed.", Duration = sw.Elapsed };
        }
    }
}
