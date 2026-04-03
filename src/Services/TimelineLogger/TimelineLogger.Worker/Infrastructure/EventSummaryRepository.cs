using MongoDB.Driver;
using TimelineLogger.Worker.Infrastructure.Documents;

namespace TimelineLogger.Worker.Infrastructure;

public sealed class EventSummaryRepository
{
    private readonly MongoDbContext _ctx;

    public EventSummaryRepository(MongoDbContext ctx) => _ctx = ctx;

    public async Task UpsertAsync(EventSummaryDocument summary, CancellationToken ct = default)
    {
        var filter = Builders<EventSummaryDocument>.Filter.And(
            Builders<EventSummaryDocument>.Filter.Eq(x => x.Id.Symbol, summary.Symbol),
            Builders<EventSummaryDocument>.Filter.Eq(x => x.Id.Date, summary.Date));

        summary.UpdatedAt = DateTime.UtcNow;
        await _ctx.EventSummaries.ReplaceOneAsync(filter, summary,
            new ReplaceOptions { IsUpsert = true }, ct);
    }

    public async Task<EventSummaryDocument?> GetAsync(string symbol, string date, CancellationToken ct = default)
    {
        var filter = Builders<EventSummaryDocument>.Filter.And(
            Builders<EventSummaryDocument>.Filter.Eq(x => x.Id.Symbol, symbol),
            Builders<EventSummaryDocument>.Filter.Eq(x => x.Id.Date, date));

        return await _ctx.EventSummaries.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<List<EventSummaryDocument>> GetRangeAsync(string symbol, string startDate, string endDate, CancellationToken ct = default)
    {
        var filter = Builders<EventSummaryDocument>.Filter.And(
            Builders<EventSummaryDocument>.Filter.Eq(x => x.Id.Symbol, symbol),
            Builders<EventSummaryDocument>.Filter.Gte(x => x.Date, startDate),
            Builders<EventSummaryDocument>.Filter.Lte(x => x.Date, endDate));

        return await _ctx.EventSummaries
            .Find(filter)
            .Sort(Builders<EventSummaryDocument>.Sort.Descending(x => x.Date))
            .ToListAsync(ct);
    }

    public async Task<List<EventSummaryDocument>> GetLatestForAllSymbolsAsync(int days, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
        var filter = Builders<EventSummaryDocument>.Filter.Gte(x => x.Date, since);
        return await _ctx.EventSummaries.Find(filter).ToListAsync(ct);
    }
}
