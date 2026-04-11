using Dapper;
using HouseKeeper.Worker.Configuration;
using HouseKeeper.Worker.Jobs;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HouseKeeper.Worker;

public sealed class HouseKeeperWorker(
    IEnumerable<ICleanupJob> jobs,
    IOptions<HouseKeeperSettings> options,
    ILogger<HouseKeeperWorker> logger,
    string connectionString) : BackgroundService
{
    private readonly HouseKeeperSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            logger.LogWarning("HouseKeeper is disabled via config (HouseKeeper__Enabled=false). Exiting.");
            return;
        }

        logger.LogInformation(
            "HouseKeeper starting. DryRun={DryRun}, Schedule={Schedule} UTC, Retention=[orders={O}d, gaps={G}d, ticks={T}mo], BatchSize={B}",
            _settings.DryRun, _settings.ScheduleUtc,
            _settings.Retention.OrdersDays, _settings.Retention.FilledGapsDays, _settings.Retention.PriceTicksMonths,
            _settings.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNextRun(_settings.ScheduleUtc);
            logger.LogInformation("HouseKeeper sleeping {Minutes:F1} minutes until next run at {Time:HH:mm} UTC.", delay.TotalMinutes, DateTime.UtcNow.Add(delay));

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            await RunAllJobsAsync(stoppingToken);
        }

        logger.LogInformation("HouseKeeper stopped.");
    }

    private async Task RunAllJobsAsync(CancellationToken ct)
    {
        var runStart = DateTime.UtcNow;
        var deadline = runStart.AddSeconds(_settings.MaxRunSeconds);
        var dryRun = _settings.DryRun;
        var results = new List<JobResult>();

        logger.LogInformation("=== HouseKeeper run started at {Time:u} (DryRun={DryRun}) ===", runStart, dryRun);

        foreach (var job in jobs)
        {
            if (ct.IsCancellationRequested || DateTime.UtcNow >= deadline)
            {
                logger.LogWarning("HouseKeeper: time budget exhausted before running {Job}.", job.Name);
                break;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(deadline - DateTime.UtcNow);

            var result = await job.RunAsync(dryRun, cts.Token);
            results.Add(result);

            if (result.Success)
                logger.LogInformation("Job {Job}: {Summary} ({Duration:F1}s)", result.JobName, result.Summary, result.Duration.TotalSeconds);
            else
                logger.LogError("Job {Job} FAILED: {Error}", result.JobName, result.Error);
        }

        await PersistAuditLogAsync(results, runStart, ct);

        logger.LogInformation("=== HouseKeeper run completed in {Elapsed:F1}s ===", (DateTime.UtcNow - runStart).TotalSeconds);
    }

    private async Task PersistAuditLogAsync(List<JobResult> results, DateTime runStart, CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            const string sql = """
                INSERT INTO public.housekeeper_audit_log
                    (run_at_utc, job_name, dry_run, rows_affected, summary, error, duration_ms)
                VALUES
                    (@RunAtUtc, @JobName, @DryRun, @RowsAffected, @Summary, @Error, @DurationMs)
                """;

            foreach (var r in results)
            {
                await conn.ExecuteAsync(sql, new
                {
                    RunAtUtc = runStart,
                    r.JobName,
                    r.DryRun,
                    r.RowsAffected,
                    Summary = r.Summary[..Math.Min(r.Summary.Length, 2000)],
                    r.Error,
                    DurationMs = (long)r.Duration.TotalMilliseconds
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist HouseKeeper audit log — run results are in structured logs.");
        }
    }

    private static TimeSpan ComputeDelayUntilNextRun(string scheduleUtc)
    {
        if (!TimeOnly.TryParse(scheduleUtc, out var target))
            target = new TimeOnly(3, 15);

        var now = DateTime.UtcNow;
        var todayRun = now.Date.Add(target.ToTimeSpan());
        var nextRun = todayRun > now ? todayRun : todayRun.AddDays(1);
        return nextRun - now;
    }
}
