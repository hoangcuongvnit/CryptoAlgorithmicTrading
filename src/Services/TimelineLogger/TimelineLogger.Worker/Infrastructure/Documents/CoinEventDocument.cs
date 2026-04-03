using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace TimelineLogger.Worker.Infrastructure.Documents;

public sealed class CoinEventDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [BsonElement("event_type")]
    public string EventType { get; set; } = string.Empty;

    [BsonElement("event_category")]
    public string EventCategory { get; set; } = string.Empty;

    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; }

    [BsonElement("unix_timestamp")]
    public long UnixTimestamp { get; set; }

    [BsonElement("source_service")]
    public string SourceService { get; set; } = string.Empty;

    [BsonElement("severity")]
    public string Severity { get; set; } = "INFO";

    [BsonElement("correlation_id")]
    public string CorrelationId { get; set; } = string.Empty;

    [BsonElement("session_id")]
    public string? SessionId { get; set; }

    [BsonElement("payload")]
    public BsonDocument Payload { get; set; } = new();

    [BsonElement("metadata")]
    public BsonDocument Metadata { get; set; } = new();

    [BsonElement("tags")]
    public List<string> Tags { get; set; } = new();

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("expires_at")]
    public DateTime ExpiresAt { get; set; }
}
