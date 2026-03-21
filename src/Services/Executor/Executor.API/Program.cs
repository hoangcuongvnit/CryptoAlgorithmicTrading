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

// Recovery services — RecoveryStateService must be registered before hosted services that depend on it
builder.Services.AddSingleton<RecoveryStateService>();
builder.Services.AddHostedService<StartupReconciliationService>();
builder.Services.AddHostedService<LiquidationOrchestrator>();

// Shutdown/close-all services
builder.Services.AddSingleton<ShutdownOperationService>();
builder.Services.AddSingleton<CloseAllExecutorService>();
builder.Services.AddHostedService<CloseAllSchedulerService>();

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

// ── 4-Hour Session Report endpoints ──────────────────────────────────────

app.MapGet("/api/trading/report/sessions/daily", async (
    [FromServices] OrderRepository repo,
    [FromQuery] string? date,
    [FromQuery] string? mode,
    CancellationToken ct) =>
{
    var reportDate = DateTime.TryParse(date, out var parsed) ? parsed : DateTime.UtcNow.Date;
    bool? isPaper = mode switch { "paper" => true, "live" => false, _ => null };
    var sessions = await repo.GetSessionDailyReportAsync(reportDate, isPaper, ct);
    return Results.Ok(sessions);
});

app.MapGet("/api/trading/report/sessions/range", async (
    [FromServices] OrderRepository repo,
    [FromQuery] string? from,
    [FromQuery] string? to,
    [FromQuery] string? mode,
    CancellationToken ct) =>
{
    var fromDate = DateTime.TryParse(from, out var pFrom) ? pFrom : DateTime.UtcNow.Date.AddDays(-6);
    var toDate = DateTime.TryParse(to, out var pTo) ? pTo : DateTime.UtcNow.Date;
    bool? isPaper = mode switch { "paper" => true, "live" => false, _ => null };
    var sessions = await repo.GetSessionRangeReportAsync(fromDate, toDate, isPaper, ct);
    return Results.Ok(sessions);
});

app.MapGet("/api/trading/report/sessions/{sessionId}/symbols", async (
    [FromServices] OrderRepository repo,
    string sessionId,
    [FromQuery] string? mode,
    CancellationToken ct) =>
{
    bool? isPaper = mode switch { "paper" => true, "live" => false, _ => null };
    var symbols = await repo.GetSessionSymbolsAsync(sessionId, isPaper, ct);
    return Results.Ok(symbols);
});

app.MapGet("/api/trading/report/sessions/equity-curve", async (
    [FromServices] OrderRepository repo,
    [FromQuery] string? from,
    [FromQuery] string? to,
    [FromQuery] string? mode,
    CancellationToken ct) =>
{
    var fromDate = DateTime.TryParse(from, out var pFrom) ? pFrom : DateTime.UtcNow.Date.AddDays(-6);
    var toDate = DateTime.TryParse(to, out var pTo) ? pTo : DateTime.UtcNow.Date;
    bool? isPaper = mode switch { "paper" => true, "live" => false, _ => null };
    var curve = await repo.GetSessionEquityCurveAsync(fromDate, toDate, isPaper, ct);
    return Results.Ok(curve);
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

// ── Trading Control (close-all / shutdown) endpoints ─────────────────────

app.MapPost("/api/trading/control/close-all", async (
    [FromServices] ShutdownOperationService shutdownOp,
    [FromServices] CloseAllExecutorService executor,
    [FromServices] PositionTracker tracker,
    HttpRequest request,
    CancellationToken ct) =>
{
    var body = await request.ReadFromJsonAsync<CloseAllRequest>(ct);
    if (body is null)
        return Results.BadRequest(new { error = "Request body is required." });

    if (!string.Equals(body.ConfirmationToken, "CLOSE ALL", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Invalid confirmation token. Type 'CLOSE ALL' to confirm." });

    if (string.IsNullOrWhiteSpace(body.IdempotencyKey))
        return Results.BadRequest(new { error = "idempotencyKey is required." });

    var (success, error, operationId) = shutdownOp.RequestCloseAll(
        body.Reason ?? "manual",
        body.RequestedBy ?? "operator",
        body.IdempotencyKey);

    if (!success)
        return Results.Conflict(new { error });

    var positionCount = tracker.GetOpenPositions().Count;

    // Fire and forget — the executor reports progress via ShutdownOperationService
    _ = Task.Run(() => executor.ExecuteCloseAllAsync(operationId, CancellationToken.None));

    return Results.Accepted("/api/trading/control/close-all/status", new
    {
        operationId,
        status = "Executing",
        openPositions = positionCount,
        message = "Close-all started. Poll /api/trading/control/close-all/status for updates."
    });
});

app.MapPost("/api/trading/control/close-all/schedule", async (
    [FromServices] ShutdownOperationService shutdownOp,
    [FromServices] PositionTracker tracker,
    HttpRequest request,
    CancellationToken ct) =>
{
    var body = await request.ReadFromJsonAsync<ScheduleCloseAllRequest>(ct);
    if (body is null)
        return Results.BadRequest(new { error = "Request body is required." });

    if (!string.Equals(body.ConfirmationToken, "CLOSE ALL", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Invalid confirmation token. Type 'CLOSE ALL' to confirm." });

    if (string.IsNullOrWhiteSpace(body.IdempotencyKey))
        return Results.BadRequest(new { error = "idempotencyKey is required." });

    var (success, error, operationId) = shutdownOp.ScheduleCloseAll(
        body.ExecuteAtUtc,
        body.Reason ?? "scheduled_shutdown",
        body.RequestedBy ?? "operator",
        body.IdempotencyKey);

    if (!success)
        return Results.Conflict(new { error });

    var positionCount = tracker.GetOpenPositions().Count;
    return Results.Accepted("/api/trading/control/close-all/status", new
    {
        operationId,
        status = "Scheduled",
        executeAtUtc = body.ExecuteAtUtc,
        openPositions = positionCount,
        message = $"Close-all scheduled for {body.ExecuteAtUtc:u}. System will enter exit-only mode at execution time."
    });
});

app.MapPost("/api/trading/control/close-all/cancel", async (
    [FromServices] ShutdownOperationService shutdownOp,
    HttpRequest request,
    CancellationToken ct) =>
{
    var body = await request.ReadFromJsonAsync<CancelCloseAllRequest>(ct);
    if (body is null || !Guid.TryParse(body.OperationId, out var operationId))
        return Results.BadRequest(new { error = "operationId (UUID) is required." });

    var (success, error) = shutdownOp.TryCancel(operationId);
    return success
        ? Results.Ok(new { operationId, status = "Canceled" })
        : Results.Conflict(new { error });
});

app.MapGet("/api/trading/control/close-all/status", ([FromServices] ShutdownOperationService shutdownOp,
    [FromServices] PositionTracker tracker) =>
{
    var op = shutdownOp.Current;
    return Results.Ok(new
    {
        operationId = op.OperationId == Guid.Empty ? (Guid?)null : op.OperationId,
        status = op.Status,
        operationType = op.OperationType,
        requestedBy = op.RequestedBy,
        reason = op.Reason,
        requestedAtUtc = op.RequestedAtUtc == default ? (DateTime?)null : op.RequestedAtUtc,
        scheduledForUtc = op.ScheduledForUtc,
        startedAtUtc = op.StartedAtUtc,
        completedAtUtc = op.CompletedAtUtc,
        shutdownReady = op.ShutdownReady,
        positionsClosedCount = op.PositionsClosedCount,
        openPositionsRemaining = tracker.GetOpenPositions().Count,
        exitOnlyMode = shutdownOp.IsExitOnlyMode,
        lastError = op.LastError
    });
});

app.MapGet("/api/trading/control/close-all/history", async (
    [FromServices] ShutdownOperationService shutdownOp,
    [FromQuery] int? limit,
    CancellationToken ct) =>
{
    var history = await shutdownOp.GetHistoryAsync(limit ?? 20, ct);
    return Results.Ok(history);
});

app.Run();

record CloseAllRequest(
    string? Reason,
    string? RequestedBy,
    string ConfirmationToken,
    string IdempotencyKey);

record ScheduleCloseAllRequest(
    DateTime ExecuteAtUtc,
    string? Reason,
    string? RequestedBy,
    string ConfirmationToken,
    string IdempotencyKey);

record CancelCloseAllRequest(string OperationId);
