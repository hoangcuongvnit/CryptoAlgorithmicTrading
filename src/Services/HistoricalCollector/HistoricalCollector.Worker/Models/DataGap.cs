namespace HistoricalCollector.Worker.Models;

public sealed record DataGap
{
    public long Id { get; init; }
    public required string Symbol { get; init; }
    public required string Interval { get; init; }
    public required DateTime GapStart { get; init; }
    public required DateTime GapEnd { get; init; }
    public required DateTime DetectedAt { get; init; }
    public DateTime? FilledAt { get; init; }
}
