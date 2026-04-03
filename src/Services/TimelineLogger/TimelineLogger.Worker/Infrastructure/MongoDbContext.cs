using Microsoft.Extensions.Options;
using MongoDB.Driver;
using TimelineLogger.Worker.Configuration;
using TimelineLogger.Worker.Infrastructure.Documents;

namespace TimelineLogger.Worker.Infrastructure;

public sealed class MongoDbContext
{
    private readonly IMongoDatabase _db;

    public MongoDbContext(IOptions<MongoSettings> settings)
    {
        var mongoSettings = settings.Value;
        var client = new MongoClient(mongoSettings.ConnectionString);
        _db = client.GetDatabase(mongoSettings.Database);
    }

    public IMongoCollection<CoinEventDocument> CoinEvents =>
        _db.GetCollection<CoinEventDocument>("coin_events");

    public IMongoCollection<EventSummaryDocument> EventSummaries =>
        _db.GetCollection<EventSummaryDocument>("event_summary");

    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        // coin_events indexes
        var eventsIndexes = CoinEvents.Indexes;

        await eventsIndexes.CreateManyAsync([
            new CreateIndexModel<CoinEventDocument>(
                Builders<CoinEventDocument>.IndexKeys
                    .Ascending(x => x.Symbol)
                    .Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "symbol_timestamp" }),
            new CreateIndexModel<CoinEventDocument>(
                Builders<CoinEventDocument>.IndexKeys
                    .Ascending(x => x.Symbol)
                    .Ascending(x => x.EventType)
                    .Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "symbol_eventtype_timestamp" }),
            new CreateIndexModel<CoinEventDocument>(
                Builders<CoinEventDocument>.IndexKeys
                    .Ascending(x => x.EventCategory)
                    .Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "category_timestamp" }),
            new CreateIndexModel<CoinEventDocument>(
                Builders<CoinEventDocument>.IndexKeys
                    .Ascending(x => x.SourceService)
                    .Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "source_timestamp" }),
            new CreateIndexModel<CoinEventDocument>(
                Builders<CoinEventDocument>.IndexKeys
                    .Ascending(x => x.SessionId)
                    .Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "session_timestamp" }),
            new CreateIndexModel<CoinEventDocument>(
                Builders<CoinEventDocument>.IndexKeys.Ascending(x => x.ExpiresAt),
                new CreateIndexOptions { Name = "ttl", ExpireAfter = TimeSpan.Zero }),
        ], ct);

        // event_summary indexes
        await EventSummaries.Indexes.CreateOneAsync(
            new CreateIndexModel<EventSummaryDocument>(
                Builders<EventSummaryDocument>.IndexKeys
                    .Ascending(x => x.Symbol)
                    .Descending(x => x.Date),
                new CreateIndexOptions { Name = "symbol_date" }), cancellationToken: ct);
    }
}
