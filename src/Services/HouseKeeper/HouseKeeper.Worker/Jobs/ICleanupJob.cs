namespace HouseKeeper.Worker.Jobs;

public interface ICleanupJob
{
    string Name { get; }
    Task<JobResult> RunAsync(bool dryRun, CancellationToken ct);
}

public sealed class JobResult
{
    public string JobName { get; init; } = string.Empty;
    public bool DryRun { get; init; }
    public long RowsAffected { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? Error { get; init; }
    public bool Success => Error is null;
    public DateTime RunAtUtc { get; init; } = DateTime.UtcNow;
    public TimeSpan Duration { get; init; }
}
