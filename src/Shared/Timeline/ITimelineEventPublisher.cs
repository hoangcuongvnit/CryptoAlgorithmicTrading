namespace CryptoTrading.Shared.Timeline;

public interface ITimelineEventPublisher
{
    Task PublishAsync(TimelineEvent evt, CancellationToken ct = default);
}
