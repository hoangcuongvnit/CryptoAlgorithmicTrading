using MongoDB.Bson;
using MongoDB.Driver;
using TimelineLogger.Worker.Infrastructure.Documents;

namespace TimelineLogger.Worker.Infrastructure;

public sealed class CoinEventRepository
{
    private readonly MongoDbContext _ctx;

    public CoinEventRepository(MongoDbContext ctx) => _ctx = ctx;

    public async Task InsertManyAsync(IEnumerable<CoinEventDocument> docs, CancellationToken ct = default)
    {
        var list = docs.ToList();
        if (list.Count == 0) return;
        await _ctx.CoinEvents.InsertManyAsync(list, cancellationToken: ct);
    }

    public async Task<(List<CoinEventDocument> Items, long Total)> QueryAsync(
        string symbol,
        DateTime? startTime,
        DateTime? endTime,
        string? eventType,
        string? eventCategory,
        string? sourceService,
        string? severity,
        int limit = 100,
        int offset = 0,
        bool descending = true,
        CancellationToken ct = default)
    {
        var filter = BuildFilter(symbol, startTime, endTime, eventType, eventCategory, sourceService, severity);

        var sort = descending
            ? Builders<CoinEventDocument>.Sort.Descending(x => x.Timestamp)
            : Builders<CoinEventDocument>.Sort.Ascending(x => x.Timestamp);

        var total = await _ctx.CoinEvents.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _ctx.CoinEvents
            .Find(filter)
            .Sort(sort)
            .Skip(offset)
            .Limit(limit)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<List<CoinEventDocument>> GetRecentAsync(string symbol, int limit = 50, CancellationToken ct = default)
    {
        var filter = Builders<CoinEventDocument>.Filter.Eq(x => x.Symbol, symbol);
        return await _ctx.CoinEvents
            .Find(filter)
            .Sort(Builders<CoinEventDocument>.Sort.Descending(x => x.Timestamp))
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<string, int>> GetEventCountsAsync(string symbol, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var filter = Builders<CoinEventDocument>.Filter.And(
            Builders<CoinEventDocument>.Filter.Eq(x => x.Symbol, symbol),
            Builders<CoinEventDocument>.Filter.Gte(x => x.Timestamp, from),
            Builders<CoinEventDocument>.Filter.Lt(x => x.Timestamp, to));

        var pipeline = _ctx.CoinEvents.Aggregate()
            .Match(filter)
            .Group(e => e.EventType, g => new { EventType = g.Key, Count = g.Count() });

        var results = await pipeline.ToListAsync(ct);

        return results.ToDictionary(r => r.EventType, r => r.Count);
    }

    public async Task<List<string>> GetDistinctSymbolsAsync(CancellationToken ct = default)
    {
        return (await _ctx.CoinEvents.DistinctAsync<string>("symbol", FilterDefinition<CoinEventDocument>.Empty, cancellationToken: ct))
            .ToList();
    }

    public async Task<long> GetTotalCountAsync(CancellationToken ct = default) =>
        await _ctx.CoinEvents.CountDocumentsAsync(FilterDefinition<CoinEventDocument>.Empty, cancellationToken: ct);

    public async Task<long> GetCollectionSizeAsync(CancellationToken ct = default)
    {
        var stats = await _ctx.CoinEvents.Database.RunCommandAsync<BsonDocument>(
            new BsonDocument("collStats", "coin_events"), cancellationToken: ct);
        return stats.TryGetValue("size", out var size) ? size.AsInt64 : 0;
    }

    private static FilterDefinition<CoinEventDocument> BuildFilter(
        string symbol,
        DateTime? startTime,
        DateTime? endTime,
        string? eventType,
        string? eventCategory,
        string? sourceService,
        string? severity)
    {
        var filters = new List<FilterDefinition<CoinEventDocument>>
        {
            Builders<CoinEventDocument>.Filter.Eq(x => x.Symbol, symbol)
        };

        if (startTime.HasValue)
            filters.Add(Builders<CoinEventDocument>.Filter.Gte(x => x.Timestamp, startTime.Value));
        if (endTime.HasValue)
            filters.Add(Builders<CoinEventDocument>.Filter.Lt(x => x.Timestamp, endTime.Value));
        if (!string.IsNullOrWhiteSpace(eventType))
            filters.Add(Builders<CoinEventDocument>.Filter.Eq(x => x.EventType, eventType));
        if (!string.IsNullOrWhiteSpace(eventCategory))
            filters.Add(Builders<CoinEventDocument>.Filter.Eq(x => x.EventCategory, eventCategory));
        if (!string.IsNullOrWhiteSpace(sourceService))
            filters.Add(Builders<CoinEventDocument>.Filter.Eq(x => x.SourceService, sourceService));
        if (!string.IsNullOrWhiteSpace(severity))
            filters.Add(Builders<CoinEventDocument>.Filter.Eq(x => x.Severity, severity));

        return Builders<CoinEventDocument>.Filter.And(filters);
    }
}
