namespace CryptoTrading.Shared.Timeline;

public sealed class NullTimelineEventPublisher : ITimelineEventPublisher
{
    public Task PublishAsync(TimelineEvent evt, CancellationToken ct = default) => Task.CompletedTask;
}
