using CsvHelper;
using MongoDB.Driver;
using StackExchange.Redis;
using System.Globalization;
using TimelineLogger.Worker.Configuration;
using TimelineLogger.Worker.Infrastructure;
using TimelineLogger.Worker.Services;
using TimelineLogger.Worker.Workers;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("MongoDB"));
builder.Services.Configure<TimelineSettings>(builder.Configuration.GetSection("Timeline"));

// Redis
var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

// MongoDB
builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<CoinEventRepository>();
builder.Services.AddSingleton<EventSummaryRepository>();

// Services
builder.Services.AddSingleton<TimelineQueryService>();

// Workers
builder.Services.AddSingleton<CoinEventLoggerWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CoinEventLoggerWorker>());

var app = builder.Build();

// Initialize MongoDB indexes on startup
await app.Services.GetRequiredService<MongoDbContext>().EnsureIndexesAsync();

// ── GET /api/timeline/events ──────────────────────────────────────────────
app.MapGet("/api/timeline/events", async (
    string symbol,
    string? startDate,
    string? endDate,
    string? startTime,
    string? endTime,
    string? eventType,
    string? eventCategory,
    string? sourceService,
    string? severity,
    int limit = 100,
    int offset = 0,
    string sortOrder = "desc",
    TimelineQueryService query = default!,
    CancellationToken ct = default) =>
{
    if (string.IsNullOrWhiteSpace(symbol))
        return Results.BadRequest("symbol is required");

    limit = Math.Clamp(limit, 1, 1000);

    DateTime? from = ParseDateTime(startTime) ?? ParseDate(startDate);
    DateTime? to = ParseDateTime(endTime) ?? (ParseDate(endDate)?.AddDays(1));
    bool desc = !string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);

    var (items, total) = await query.GetEventsAsync(
        symbol, from, to, eventType, eventCategory, sourceService, severity,
        limit, offset, desc, ct);

    return Results.Ok(new
    {
        status = "success",
        total,
        limit,
        offset,
        symbol,
        period = new { start = from, end = to },
        data = items.Select(e => new
        {
            id = e.Id,
            symbol = e.Symbol,
            timestamp = e.Timestamp,
            unix_timestamp = e.UnixTimestamp,
            event_type = e.EventType,
            event_category = e.EventCategory,
            source_service = e.SourceService,
            severity = e.Severity,
            correlation_id = e.CorrelationId,
            session_id = e.SessionId,
            payload = e.Payload,
            metadata = e.Metadata,
            tags = e.Tags,
        }),
        timestamp = DateTime.UtcNow
    });
});

// ── GET /api/timeline/summary ─────────────────────────────────────────────
app.MapGet("/api/timeline/summary", async (
    string symbol,
    string? date,
    string? startDate,
    string? endDate,
    string period = "daily",
    TimelineQueryService query = default!,
    CancellationToken ct = default) =>
{
    if (string.IsNullOrWhiteSpace(symbol))
        return Results.BadRequest("symbol is required");

    if (!string.IsNullOrWhiteSpace(startDate) && !string.IsNullOrWhiteSpace(endDate))
    {
        var range = await query.GetRangeSummaryAsync(symbol, startDate, endDate, ct);
        return Results.Ok(new { status = "success", symbol, summaries = range });
    }

    var d = date ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
    var summary = await query.GetDailySummaryAsync(symbol, d, ct);

    if (summary is null)
        return Results.Ok(new { status = "success", symbol, summary = (object?)null });

    return Results.Ok(new
    {
        status = "success",
        symbol,
        summary = new
        {
            period = "daily",
            date = summary.Date,
            total_events = summary.TotalEvents,
            event_breakdown = summary.EventCounts,
            trading_metrics = new
            {
                orders_placed = summary.OrdersPlaced,
                orders_filled = summary.OrdersFilled,
            },
            signals = new
            {
                strong = summary.SignalsStrong,
                neutral = summary.SignalsNeutral,
                weak = summary.SignalsWeak,
            },
            risk = new
            {
                approvals = summary.RiskApprovals,
                rejections = summary.RiskRejections,
            }
        }
    });
});

// ── GET /api/timeline/dashboard ───────────────────────────────────────────
app.MapGet("/api/timeline/dashboard", async (
    int days = 7,
    TimelineQueryService query = default!,
    CancellationToken ct = default) =>
{
    var data = await query.GetDashboardDataAsync(days, ct);

    var bySymbol = data
        .GroupBy(s => s.Symbol)
        .Select(g => new
        {
            symbol = g.Key,
            total_events = g.Sum(s => s.TotalEvents),
            orders_placed = g.Sum(s => s.OrdersPlaced),
            orders_filled = g.Sum(s => s.OrdersFilled),
            signals_strong = g.Sum(s => s.SignalsStrong),
            signals_neutral = g.Sum(s => s.SignalsNeutral),
            signals_weak = g.Sum(s => s.SignalsWeak),
            last_updated = g.Max(s => s.UpdatedAt),
        });

    return Results.Ok(new
    {
        status = "success",
        days,
        coin_summaries = bySymbol
    });
});

// ── GET /api/timeline/export ──────────────────────────────────────────────
app.MapGet("/api/timeline/export", async (
    string symbol,
    string startDate,
    string endDate,
    string format = "csv",
    TimelineQueryService query = default!,
    CancellationToken ct = default) =>
{
    if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
        return Results.BadRequest("symbol, startDate, endDate are required");

    var from = ParseDate(startDate);
    var to = ParseDate(endDate)?.AddDays(1);
    var (items, _) = await query.GetEventsAsync(symbol, from, to, null, null, null, null, 10000, 0, true, ct);

    if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Json(new { symbol, start = startDate, end = endDate, data = items });
    }

    // CSV export
    using var ms = new MemoryStream();
    using var sw = new StreamWriter(ms, leaveOpen: true);
    using var csv = new CsvWriter(sw, CultureInfo.InvariantCulture);

    csv.WriteHeader<ExportRow>();
    await csv.NextRecordAsync();
    foreach (var item in items)
    {
        csv.WriteRecord(new ExportRow(
            item.Symbol, item.Timestamp, item.EventType,
            item.EventCategory, item.SourceService, item.Severity,
            item.CorrelationId, item.SessionId ?? ""));
        await csv.NextRecordAsync();
    }
    await sw.FlushAsync(ct);

    ms.Position = 0;
    var bytes = ms.ToArray();
    var filename = $"timeline_{symbol}_{startDate}_{endDate}.csv";
    return Results.File(bytes, "text/csv", filename);
});

// ── GET /api/timeline/health ──────────────────────────────────────────────
app.MapGet("/api/timeline/health", async (
    CoinEventLoggerWorker worker,
    IConnectionMultiplexer redis,
    CancellationToken ct = default) =>
{
    var redisOk = false;
    var redisLatency = 0L;
    try
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await redis.GetDatabase().PingAsync();
        redisLatency = sw.ElapsedMilliseconds;
        redisOk = true;
    }
    catch { /* swallow */ }

    return Results.Ok(new
    {
        status = "healthy",
        service = "TimelineLogger.Worker",
        redis = new { connected = redisOk, latency_ms = redisLatency },
        event_processing = new
        {
            processed_total = worker.ProcessedCount,
            error_total = worker.ErrorCount,
            queue_size = worker.QueueSize,
        }
    });
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "TimelineLogger.Worker" }));

app.Run();

// ── Helpers ───────────────────────────────────────────────────────────────

static DateTime? ParseDate(string? s) =>
    DateTime.TryParse(s, out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;

static DateTime? ParseDateTime(string? s) =>
    DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToUniversalTime() : null;

record ExportRow(string Symbol, DateTime Timestamp, string EventType,
    string EventCategory, string SourceService, string Severity,
    string CorrelationId, string SessionId);
