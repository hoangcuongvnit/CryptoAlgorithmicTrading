using TimelineLogger.Worker.Infrastructure;
using TimelineLogger.Worker.Infrastructure.Documents;

namespace TimelineLogger.Worker.Services;

public sealed class TimelineQueryService
{
    private readonly CoinEventRepository _events;
    private readonly EventSummaryRepository _summaries;

    public TimelineQueryService(CoinEventRepository events, EventSummaryRepository summaries)
    {
        _events = events;
        _summaries = summaries;
    }

    public Task<(List<CoinEventDocument> Items, long Total)> GetEventsAsync(
        string symbol,
        DateTime? startTime,
        DateTime? endTime,
        string? eventType,
        string? eventCategory,
        string? sourceService,
        string? severity,
        int limit,
        int offset,
        bool descending,
        CancellationToken ct) =>
        _events.QueryAsync(symbol, startTime, endTime, eventType, eventCategory, sourceService, severity, limit, offset, descending, ct);

    public Task<EventSummaryDocument?> GetDailySummaryAsync(string symbol, string date, CancellationToken ct) =>
        _summaries.GetAsync(symbol, date, ct);

    public Task<List<EventSummaryDocument>> GetRangeSummaryAsync(string symbol, string startDate, string endDate, CancellationToken ct) =>
        _summaries.GetRangeAsync(symbol, startDate, endDate, ct);

    public Task<List<EventSummaryDocument>> GetDashboardDataAsync(int days, CancellationToken ct) =>
        _summaries.GetLatestForAllSymbolsAsync(days, ct);

    public async Task<object> GetHealthDataAsync(CancellationToken ct)
    {
        var count = await _events.GetTotalCountAsync(ct);
        return new { count };
    }
}
