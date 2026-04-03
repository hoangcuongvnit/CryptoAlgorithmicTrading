using MongoDB.Bson.Serialization.Attributes;

namespace TimelineLogger.Worker.Infrastructure.Documents;

public sealed class EventSummaryDocument
{
    [BsonId]
    public SummaryId Id { get; set; } = new();

    [BsonElement("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [BsonElement("date")]
    public string Date { get; set; } = string.Empty;

    [BsonElement("total_events")]
    public int TotalEvents { get; set; }

    [BsonElement("event_counts")]
    public Dictionary<string, int> EventCounts { get; set; } = new();

    [BsonElement("signals_strong")]
    public int SignalsStrong { get; set; }

    [BsonElement("signals_neutral")]
    public int SignalsNeutral { get; set; }

    [BsonElement("signals_weak")]
    public int SignalsWeak { get; set; }

    [BsonElement("orders_placed")]
    public int OrdersPlaced { get; set; }

    [BsonElement("orders_filled")]
    public int OrdersFilled { get; set; }

    [BsonElement("risk_approvals")]
    public int RiskApprovals { get; set; }

    [BsonElement("risk_rejections")]
    public int RiskRejections { get; set; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SummaryId
{
    [BsonElement("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [BsonElement("date")]
    public string Date { get; set; } = string.Empty;
}
