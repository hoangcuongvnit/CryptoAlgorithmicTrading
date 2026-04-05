using Binance.Net;
using Binance.Net.Clients;
using CryptoExchange.Net.Authentication;
using CryptoTrading.Shared.Session;
using CryptoTrading.Shared.Timeline;
using Executor.API.Configuration;
using Executor.API.Infrastructure;
using Executor.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Net.Http.Json;
using StackExchange.Redis;

// Npgsql 6+ strict mode rejects DateTime(Kind=Utc) for TIMESTAMP WITHOUT TIME ZONE columns.
// The orders table uses TIMESTAMP, so enable legacy behavior to treat all DateTime values
// as plain timestamps (UTC values stored/read without timezone conversion).
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

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

var tradingBootstrapSettings = builder.Configuration.GetSection("Trading").Get<TradingSettings>() ?? new TradingSettings();
builder.Services.AddSingleton(_ => new OrderAmountLimitStore(
    tradingBootstrapSettings.MinOrderAmount > 0 ? tradingBootstrapSettings.MinOrderAmount : 5m,
    tradingBootstrapSettings.MaxNotionalPerOrder > 0 ? tradingBootstrapSettings.MaxNotionalPerOrder : 1000m));
builder.Services.AddSingleton<OrderAmountLimitValidator>();

var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

builder.Services.AddSingleton<BinanceRestClientProvider>();
builder.Services.AddSingleton<CredentialSyncGate>();
builder.Services.AddHttpClient("BybitConsensus", c => c.Timeout = TimeSpan.FromMilliseconds(500));
builder.Services.AddHttpClient("gateway", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Gateway:BaseUrl"] ?? "http://localhost:5000");
    c.Timeout = TimeSpan.FromSeconds(
        Math.Clamp(builder.Configuration.GetValue("Gateway:TimeoutSeconds", 10), 3, 60));
});
builder.Services.AddHttpClient("financialledger", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["FinancialLedger:BaseUrl"] ?? "http://localhost:5097");
    c.Timeout = TimeSpan.FromSeconds(
        Math.Clamp(builder.Configuration.GetValue("FinancialLedger:TimeoutSeconds", 5), 2, 30));
});

builder.Services.AddSingleton<PriceReferenceRepository>();
builder.Services.AddSingleton<OrderRepository>();
builder.Services.AddSingleton<BudgetRepository>();
builder.Services.AddSingleton<CashBalanceStateService>();
builder.Services.AddSingleton<AuditStreamPublisher>();
builder.Services.AddSingleton<LedgerEventPublisher>();
builder.Services.AddSingleton<SystemEventPublisher>();
builder.Services.AddSingleton<OrderWriteQueue>();
builder.Services.AddSingleton<Executor.API.Infrastructure.BinanceOrderClient>();
builder.Services.AddSingleton<SpreadFilterService>();
builder.Services.AddSingleton<PriceConsensusService>();
builder.Services.AddSingleton<BuyBudgetGuardService>();
builder.Services.AddSingleton<PositionTracker>();
builder.Services.AddSingleton(metrics);
builder.Services.AddSingleton(sessionMetrics);

// Session services
builder.Services.Configure<SessionSettings>(builder.Configuration.GetSection("Trading:Session"));
builder.Services.AddSingleton<SessionClock>();
builder.Services.AddSingleton<SessionTradingPolicy>();
builder.Services.AddSingleton<SessionBoundaryValidator>();
builder.Services.AddSingleton<ReconciliationMetrics>();
builder.Services.AddSingleton<ITimelineEventPublisher, RedisTimelineEventPublisher>();
builder.Services.AddSingleton<PositionLifecycleManager>();
builder.Services.AddSingleton<Executor.API.Services.OrderExecutionService>();

// Recovery services — RecoveryStateService must be registered before hosted services that depend on it
builder.Services.AddSingleton<RecoveryStateService>();
builder.Services.AddHostedService<OrderPersistenceWorker>();
builder.Services.AddHostedService<CredentialSyncService>();
builder.Services.AddHostedService<StartupReconciliationService>();
builder.Services.AddHostedService<PeriodicReconciliationService>();
builder.Services.AddHostedService<LiquidationOrchestrator>();
builder.Services.AddHostedService<PartialTpMonitorService>();

// Shutdown/close-all services
builder.Services.AddSingleton<ShutdownOperationService>();
builder.Services.AddSingleton<CloseAllDiscoveryService>();
builder.Services.AddSingleton<CloseAllExecutorService>();
builder.Services.AddHostedService<CloseAllSchedulerService>();
builder.Services.AddHostedService<SessionExitOnlyMonitorService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<Executor.API.Services.OrderExecutorGrpcService>();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Executor.API" }));
app.MapGet("/api/trading/reconciliation/health", ([FromServices] ReconciliationMetrics metrics, [FromServices] IOptions<TradingSettings> tradingSettings) =>
{
    var snapshot = metrics.Snapshot();
    return Results.Ok(new
    {
        enabled = tradingSettings.Value.Reconciliation.Enabled,
        snapshot
    });
});

app.MapGet("/api/trading/reconciliation/latest", async ([FromServices] OrderRepository repo, CancellationToken ct) =>
{
    var drifts = await repo.GetLatestStateDriftLogsAsync(ct);
    if (drifts.Count == 0)
    {
        return Results.Ok(new
        {
            found = false,
            message = "No reconciliation drift logs found."
        });
    }

    var latest = drifts[0];
    var total = drifts.Count;
    var recovered = drifts.Count(x => x.RecoverySuccess);
    var pendingReview = drifts.Count(x => !x.RecoverySuccess && x.RecoveryAttempted == false);

    return Results.Ok(new
    {
        found = true,
        reconciliationId = latest.ReconciliationId,
        reconciliationUtc = latest.ReconciliationUtc,
        environment = latest.Environment,
        totalDrifts = total,
        recoveredDrifts = recovered,
        pendingReviewDrifts = pendingReview,
        drifts = drifts.Select(d => new
        {
            d.Id,
            d.Symbol,
            d.DriftType,
            d.Severity,
            d.Environment,
            d.BinanceValue,
            d.LocalValue,
            d.RecoveryAction,
            d.RecoveryDetail,
            d.RecoveryAttempted,
            d.RecoverySuccess,
            d.CreatedAt
        })
    });
});

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
    [FromQuery] DateTime? from,
    [FromQuery] DateTime? to,
    CancellationToken ct) =>
{
    if (from.HasValue && to.HasValue && symbol is not null)
    {
        var orders = await repo.GetOrdersByTimeRangeAsync(symbol, from.Value, to.Value, ct);
        return Results.Ok(orders);
    }
    var recentOrders = await repo.GetRecentOrdersAsync(limit ?? 50, ct, symbol);
    return Results.Ok(recentOrders);
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
    CancellationToken ct) =>
{
    var reportDate = DateTime.TryParse(date, out var parsed) ? parsed : DateTime.UtcNow.Date;
    var sessions = await repo.GetSessionDailyReportAsync(reportDate, ct);
    return Results.Ok(sessions);
});

app.MapGet("/api/trading/report/sessions/range", async (
    [FromServices] OrderRepository repo,
    [FromQuery] string? from,
    [FromQuery] string? to,
    CancellationToken ct) =>
{
    var fromDate = DateTime.TryParse(from, out var pFrom) ? pFrom : DateTime.UtcNow.Date.AddDays(-6);
    var toDate = DateTime.TryParse(to, out var pTo) ? pTo : DateTime.UtcNow.Date;
    var sessions = await repo.GetSessionRangeReportAsync(fromDate, toDate, ct);
    return Results.Ok(sessions);
});

app.MapGet("/api/trading/report/sessions/{sessionId}/symbols", async (
    [FromServices] OrderRepository repo,
    string sessionId,
    CancellationToken ct) =>
{
    var symbols = await repo.GetSessionSymbolsAsync(sessionId, ct);
    return Results.Ok(symbols);
});

app.MapGet("/api/trading/report/sessions/equity-curve", async (
    [FromServices] OrderRepository repo,
    [FromQuery] string? from,
    [FromQuery] string? to,
    CancellationToken ct) =>
{
    var fromDate = DateTime.TryParse(from, out var pFrom) ? pFrom : DateTime.UtcNow.Date.AddDays(-6);
    var toDate = DateTime.TryParse(to, out var pTo) ? pTo : DateTime.UtcNow.Date;
    var curve = await repo.GetSessionEquityCurveAsync(fromDate, toDate, ct);
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
        liquidationStartUtc = session.LiquidationStartUtc,
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

app.MapGet("/api/trading/balance/effective", async (
    [FromServices] IHttpClientFactory httpClientFactory,
    [FromServices] IOptions<BinanceSettings> binanceOpts,
    [FromServices] IOptions<TradingSettings> tradingOpts,
    [FromServices] CashBalanceStateService cashBalanceState,
    CancellationToken ct) =>
{
    var useTestnet = binanceOpts.Value.UseTestnet;
    var environment = useTestnet ? "TESTNET" : "MAINNET";

    if (useTestnet)
    {
        var detail = "Testnet effective balance fallback is active.";
        try
        {
            var client = httpClientFactory.CreateClient("financialledger");
            var response = await client.GetAsync("/api/ledger/balance/effective?environment=TESTNET&baseCurrency=USDT", ct);
            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<FinancialLedgerEffectiveBalanceResponse>(cancellationToken: ct);
                if (payload is not null && payload.Available && payload.Balance.HasValue)
                {
                    return Results.Ok(new
                    {
                        environment,
                        source = "FINANCIAL_LEDGER",
                        available = true,
                        balance = Math.Max(0m, payload.Balance.Value),
                        asOfUtc = payload.AsOfUtc ?? DateTime.UtcNow,
                        stale = false,
                        detail = "Testnet effective balance resolved from FinancialLedger."
                    });
                }

                detail = payload?.Detail ?? "FinancialLedger returned unavailable balance payload.";
            }
            else
            {
                detail = $"FinancialLedger returned HTTP {(int)response.StatusCode}.";
            }
        }
        catch (Exception ex)
        {
            detail = $"FinancialLedger lookup failed: {ex.Message}";
        }

        return Results.Ok(new
        {
            environment,
            source = "FINANCIAL_LEDGER_FALLBACK",
            available = true,
            balance = 100m,
            asOfUtc = DateTime.UtcNow,
            stale = true,
            detail = $"{detail} Fallback balance 100 USDT applied."
        });
    }

    if (!cashBalanceState.TryGetMainnetSnapshot(out var snapshot))
    {
        return Results.Ok(new
        {
            environment,
            source = "MAINNET_RECONCILED",
            available = false,
            balance = (decimal?)null,
            asOfUtc = (DateTime?)null,
            stale = true,
            detail = "Mainnet reconciled cash snapshot is unavailable."
        });
    }

    var maxAgeMinutes = Math.Max(1, tradingOpts.Value.Reconciliation.CashSnapshotMaxAgeMinutes);
    var snapshotAge = DateTime.UtcNow - snapshot.UpdatedAtUtc;
    var isStale = snapshotAge > TimeSpan.FromMinutes(maxAgeMinutes);

    return Results.Ok(new
    {
        environment,
        source = "MAINNET_RECONCILED",
        available = true,
        balance = snapshot.CashBalance,
        asOfUtc = snapshot.UpdatedAtUtc,
        stale = isStale,
        detail = snapshot.Source,
        snapshotAgeMinutes = Math.Round(snapshotAge.TotalMinutes, 2),
        snapshotMaxAgeMinutes = maxAgeMinutes
    });
});

app.MapGet("/api/trading/report/capital-flow", async (
    [FromServices] BudgetRepository budget,
    [FromQuery] string? from,
    [FromQuery] string? to,
    [FromQuery] string? mode,
    CancellationToken ct) =>
{
    var fromDate = DateTime.TryParse(from, out var pFrom) ? pFrom : DateTime.UtcNow.Date;
    var toDate   = DateTime.TryParse(to,   out var pTo)   ? pTo   : DateTime.UtcNow.Date;
    var tradingMode = mode is "testnet" ? "testnet" : "live";
    var events = await budget.GetCapitalFlowAsync(fromDate, toDate, tradingMode, ct);
    return Results.Ok(events);
});

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
    [FromServices] PositionTracker tracker,
    [FromServices] SessionClock sessionClock) =>
{
    var op = shutdownOp.Current;
    var session = sessionClock.GetCurrentSession();
    var inFinal30 = session.CurrentPhase is CryptoTrading.Shared.DTOs.SessionPhase.LiquidationOnly
        or CryptoTrading.Shared.DTOs.SessionPhase.ForcedFlatten;

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
        discoveredCandidatesCount = op.DiscoveredCandidatesCount,
        attemptedCloseCount = op.AttemptedCloseCount,
        verifiedAtUtc = op.VerifiedAtUtc,
        leftovers = op.Leftovers,
        openPositionsRemaining = tracker.GetOpenPositions().Count,
        exitOnlyMode = shutdownOp.IsExitOnlyMode,
        tradingMode = shutdownOp.TradingMode,
        exitOnlySource = shutdownOp.ExitOnlySource,
        resumeAllowed = shutdownOp.ResumeAllowed,
        resumeBlockReasons = shutdownOp.GetResumeBlockReasons(),
        lastError = op.LastError,
        // Session timing fields
        sessionId = session.SessionId,
        sessionEndUtc = session.SessionEndUtc,
        inFinal30Minutes = inFinal30,
        minutesToSessionEnd = Math.Max(0, session.TimeToEnd.TotalMinutes)
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

app.MapPost("/api/trading/control/resume", async (
    [FromServices] ShutdownOperationService shutdownOp,
    HttpRequest request,
    CancellationToken ct) =>
{
    var body = await request.ReadFromJsonAsync<ResumeTradingRequest>(ct);
    if (body is null)
        return Results.BadRequest(new { error = "Request body is required." });

    if (!string.Equals(body.ConfirmationToken, "RESUME TRADING", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Invalid confirmation token. Type 'RESUME TRADING' to confirm." });

    var (success, error) = shutdownOp.TryResume(
        body.Reason ?? "operator_resume",
        body.RequestedBy ?? "operator");

    if (!success)
        return Results.Conflict(new
        {
            success = false,
            error,
            tradingMode = shutdownOp.TradingMode,
            resumeBlockReasons = shutdownOp.GetResumeBlockReasons()
        });

    return Results.Ok(new
    {
        success = true,
        tradingMode = "TradingEnabled",
        resumedAtUtc = DateTime.UtcNow,
        message = "Trading has been resumed. New entries are now allowed."
    });
});

// ── Runtime config reload endpoints (called by Gateway on settings change) ─

app.MapPost("/api/trading/reload-exchange-config", (
    [FromServices] BinanceRestClientProvider clientProvider,
    [FromServices] IOptions<BinanceSettings> binanceOpts,
    HttpRequest request,
    CancellationToken ct) =>
{
    var body = request.ReadFromJsonAsync<ExchangeReloadRequest>(ct).GetAwaiter().GetResult();
    if (body is null) return Results.BadRequest("Request body is required");

    var settings = binanceOpts.Value;
    settings.ApiKey = body.ApiKey;
    settings.ApiSecret = body.ApiSecret;
    settings.TestnetApiKey = body.TestnetApiKey;
    settings.TestnetApiSecret = body.TestnetApiSecret;
    settings.UseTestnet = body.UseTestnet;

    var activeKey = body.UseTestnet && !string.IsNullOrEmpty(body.TestnetApiKey)
        ? body.TestnetApiKey : body.ApiKey;
    var activeSecret = body.UseTestnet && !string.IsNullOrEmpty(body.TestnetApiSecret)
        ? body.TestnetApiSecret : body.ApiSecret;
    clientProvider.Reconfigure(activeKey, activeSecret, body.UseTestnet);

    return Results.Ok(new { reloaded = true, useTestnet = body.UseTestnet });
});

app.MapGet("/api/trading/order-amount-limit", ([FromServices] OrderAmountLimitStore orderAmountLimits) =>
{
    var snapshot = orderAmountLimits.Current;
    return Results.Ok(new { minOrderAmount = snapshot.MinOrderAmount, maxOrderAmount = snapshot.MaxOrderAmount, updatedAtUtc = snapshot.UpdatedAtUtc });
});

app.MapPost("/api/trading/reload-order-amount-limit", async (
    [FromServices] OrderAmountLimitStore orderAmountLimits,
    HttpRequest request,
    CancellationToken ct) =>
{
    OrderAmountLimitReloadRequest? body;
    try { body = await request.ReadFromJsonAsync<OrderAmountLimitReloadRequest>(ct); }
    catch { return Results.BadRequest("Invalid JSON body"); }
    if (body is null) return Results.BadRequest("Request body is required");
    if (body.MinOrderAmount is null) return Results.BadRequest("minOrderAmount is required");
    if (body.MaxOrderAmount is null) return Results.BadRequest("maxOrderAmount is required");
    if (body.MinOrderAmount <= 0 || body.MaxOrderAmount <= 0)
        return Results.BadRequest("minOrderAmount and maxOrderAmount must be greater than 0");
    if (body.MinOrderAmount > body.MaxOrderAmount)
        return Results.BadRequest("minOrderAmount must be less than or equal to maxOrderAmount");

    orderAmountLimits.Update(body.MinOrderAmount.Value, body.MaxOrderAmount.Value);
    return Results.Ok(new { reloaded = true, minOrderAmount = body.MinOrderAmount, maxOrderAmount = body.MaxOrderAmount });
});

app.MapPost("/api/trading/validate-exchange", async (
    HttpRequest request,
    CancellationToken ct) =>
{
    ValidateExchangeRequest? body;
    try { body = await request.ReadFromJsonAsync<ValidateExchangeRequest>(ct); }
    catch { return Results.BadRequest("Invalid JSON body"); }
    if (body is null || string.IsNullOrWhiteSpace(body.ApiKey) || string.IsNullOrWhiteSpace(body.ApiSecret))
        return Results.BadRequest("apiKey and apiSecret are required");

    try
    {
        // Create a temporary client with the correct environment to validate credentials
        using var tempClient = new BinanceRestClient(opts =>
        {
            opts.Environment = body.UseTestnet
                ? BinanceEnvironment.Testnet
                : BinanceEnvironment.Live;
            opts.ApiCredentials = new ApiCredentials(body.ApiKey, body.ApiSecret);
        });
        var result = await tempClient.SpotApi.Account.GetAccountInfoAsync(ct: ct);
        if (result.Success)
        {
            return Results.Ok(new { valid = true, message = "Binance connection successful" });
        }
        else
        {
            return Results.Ok(new { valid = false, message = result.Error?.Message ?? "Authentication failed" });
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new { valid = false, message = ex.Message });
    }
});

app.Run();

file sealed class FinancialLedgerEffectiveBalanceResponse
{
    public bool Available { get; set; }
    public decimal? Balance { get; set; }
    public DateTime? AsOfUtc { get; set; }
    public string? Detail { get; set; }
}

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

record ResumeTradingRequest(string? Reason, string? RequestedBy, string ConfirmationToken);
record ExchangeReloadRequest(string ApiKey, string ApiSecret, string TestnetApiKey, string TestnetApiSecret, bool UseTestnet);
record ValidateExchangeRequest(string ApiKey, string ApiSecret, bool UseTestnet);
record OrderAmountLimitReloadRequest(decimal? MinOrderAmount, decimal? MaxOrderAmount);
