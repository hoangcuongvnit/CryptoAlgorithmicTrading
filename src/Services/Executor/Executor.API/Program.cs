using Binance.Net.Clients;
using Binance.Net.Interfaces.Clients;
using CryptoTrading.Shared.Session;
using Executor.API.Configuration;
using Executor.API.Infrastructure;
using Executor.API.Services;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Create metrics instances first (before OTEL config)
var metrics = new OrderExecutionMetrics();
var sessionMetrics = new SessionMetrics();

// OpenTelemetry - Tracing
var tracingOtel = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddConsoleExporter();
    })
    .WithMetrics(metricsBuilder =>
    {
        metricsBuilder
            .AddMeter(metrics.GetMeter().Name)
            .AddMeter(sessionMetrics.GetMeter().Name)
            .AddAspNetCoreInstrumentation()
            .AddConsoleExporter();
    });

// Add services to the container.
builder.Services.AddGrpc();

builder.Services.Configure<TradingSettings>(builder.Configuration.GetSection("Trading"));
builder.Services.Configure<RedisSettings>(builder.Configuration.GetSection("Redis"));
builder.Services.Configure<BinanceSettings>(builder.Configuration.GetSection("Binance"));

var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

builder.Services.AddSingleton<IBinanceRestClient>(_ => new BinanceRestClient());

builder.Services.AddSingleton<PriceReferenceRepository>();
builder.Services.AddSingleton<OrderRepository>();
builder.Services.AddSingleton<AuditStreamPublisher>();
builder.Services.AddSingleton<PaperOrderSimulator>();
builder.Services.AddSingleton<BinanceOrderClient>();
builder.Services.AddSingleton<PositionTracker>();
builder.Services.AddSingleton(metrics);
builder.Services.AddSingleton(sessionMetrics);

// Session services
builder.Services.Configure<SessionSettings>(builder.Configuration.GetSection("Trading:Session"));
builder.Services.AddSingleton<SessionClock>();
builder.Services.AddSingleton<SessionTradingPolicy>();
builder.Services.AddSingleton<PositionLifecycleManager>();
builder.Services.AddSingleton<OrderExecutionService>();
builder.Services.AddHostedService<LiquidationOrchestrator>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<OrderExecutorGrpcService>();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Executor.API" }));
app.MapGet("/metrics", () => "Prometheus metrics endpoint. Configure Prometheus to scrape http://localhost:9091/metrics");
app.MapGet("/", () => "Order Executor gRPC service is running.");

// ── Trading REST API (HTTP/1.1 on port 5094) ─────────────────────────────

app.MapGet("/api/trading/stats", ([FromServices] PositionTracker tracker) =>
{
    var stats = tracker.GetStats();
    return Results.Ok(new
    {
        totalTrades = stats.TotalTrades,
        winTrades = stats.WinTrades,
        lossTrades = stats.LossTrades,
        totalPnL = stats.TotalPnL,
        winRate = stats.WinRate,
        maxDrawdown = stats.MaxDrawdown,
        avgPnLPerTrade = stats.AvgPnLPerTrade
    });
});

app.MapGet("/api/trading/positions", ([FromServices] PositionTracker tracker) =>
    Results.Ok(tracker.GetOpenPositions()));

app.MapGet("/api/trading/orders", async (
    [FromServices] OrderRepository repo,
    [FromQuery] string? symbol,
    [FromQuery] int? limit,
    CancellationToken ct) =>
{
    var orders = await repo.GetRecentOrdersAsync(limit ?? 50, ct, symbol);
    return Results.Ok(orders);
});

// ── Daily Report endpoints ────────────────────────────────────────────────

app.MapGet("/api/trading/report/daily", async (
    [FromServices] OrderRepository repo,
    [FromQuery] string? date,
    CancellationToken ct) =>
{
    var reportDate = DateTime.TryParse(date, out var parsed) ? parsed : DateTime.UtcNow.Date;
    var summary = await repo.GetDailyReportAsync(reportDate, ct);
    return Results.Ok(summary);
});

app.MapGet("/api/trading/report/daily/symbols", async (
    [FromServices] OrderRepository repo,
    [FromQuery] string? date,
    CancellationToken ct) =>
{
    var reportDate = DateTime.TryParse(date, out var parsed) ? parsed : DateTime.UtcNow.Date;
    var breakdown = await repo.GetDailySymbolBreakdownAsync(reportDate, ct);
    return Results.Ok(breakdown);
});

app.MapGet("/api/trading/report/time-analytics", async (
    [FromServices] OrderRepository repo,
    [FromQuery] string? date,
    CancellationToken ct) =>
{
    var reportDate = DateTime.TryParse(date, out var parsed) ? parsed : DateTime.UtcNow.Date;
    var trades = await repo.GetDailyTimeAnalyticsAsync(reportDate, ct);
    var closedWithDuration = trades.Where(t => t.HoldingMinutes.HasValue).ToList();

    var result = new
    {
        trades,
        avgHoldingMinutes = closedWithDuration.Count > 0 ? closedWithDuration.Average(t => t.HoldingMinutes!.Value) : 0,
        avgHoldingWinMinutes = closedWithDuration.Where(t => t.RealizedPnL > 0).Select(t => t.HoldingMinutes!.Value) is var wDur && wDur.Any() ? wDur.Average() : 0,
        avgHoldingLossMinutes = closedWithDuration.Where(t => t.RealizedPnL < 0).Select(t => t.HoldingMinutes!.Value) is var lDur && lDur.Any() ? lDur.Average() : 0,
    };
    return Results.Ok(result);
});

app.MapGet("/api/trading/report/hourly", async (
    [FromServices] OrderRepository repo,
    [FromQuery] string? date,
    CancellationToken ct) =>
{
    var reportDate = DateTime.TryParse(date, out var parsed) ? parsed : DateTime.UtcNow.Date;
    var buckets = await repo.GetHourlyBucketsAsync(reportDate, ct);
    return Results.Ok(buckets);
});

// ── Session endpoints ─────────────────────────────────────────────────────

app.MapGet("/api/trading/session", ([FromServices] SessionClock clock) =>
{
    var session = clock.GetCurrentSession();
    return Results.Ok(new
    {
        sessionId = session.SessionId,
        sessionNumber = session.SessionNumber,
        currentPhase = session.CurrentPhase.ToString(),
        sessionStartUtc = session.SessionStartUtc,
        sessionEndUtc = session.SessionEndUtc,
        minutesToEnd = session.TimeToEnd.TotalMinutes,
        minutesToLiquidation = session.TimeToLiquidation.TotalMinutes
    });
});

app.MapGet("/api/trading/session/positions", (
    [FromServices] PositionTracker tracker,
    [FromServices] SessionClock clock) =>
{
    var session = clock.GetCurrentSession();
    var positions = tracker.GetOpenPositions();
    return Results.Ok(new
    {
        sessionId = session.SessionId,
        phase = session.CurrentPhase.ToString(),
        positions
    });
});

app.Run();
