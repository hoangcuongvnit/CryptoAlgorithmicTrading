using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Session;
using Microsoft.Extensions.Options;
using RiskGuard.API.Configuration;
using RiskGuard.API.Infrastructure;
using RiskGuard.API.Rules;
using RiskGuard.API.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.Configure<RiskSettings>(builder.Configuration.GetSection("Risk"));

// Infrastructure
var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var opts = ConfigurationOptions.Parse(redisConnection);
    opts.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(opts);
});

builder.Services.AddSingleton<OrderStatsRepository>();
builder.Services.AddSingleton<SystemEventPublisher>();
builder.Services.AddSingleton<IRedisPersistenceService, RedisPersistenceService>();
builder.Services.AddSingleton<ValidationHistory>();
builder.Services.AddSingleton<IRiskEvaluationRepository, RiskEvaluationRepository>();

// Session services
builder.Services.Configure<SessionSettings>(builder.Configuration.GetSection("Trading:Session"));
builder.Services.AddSingleton<SessionClock>();
builder.Services.AddSingleton<SessionTradingPolicy>();

// Rules — evaluated in this order: recovery gate first, then session guards, then fast/cheap checks, DB-backed checks last
builder.Services.AddSingleton<IRiskRule, RecoveryWindowRule>();
builder.Services.AddSingleton<IRiskRule, NoCrossSessionCarryRule>();
builder.Services.AddSingleton<IRiskRule, SessionWindowRule>();
builder.Services.AddSingleton<IRiskRule, SymbolAllowListRule>();
builder.Services.AddSingleton<IRiskRule, QuantityRule>();
builder.Services.AddSingleton<IRiskRule, PositionSizeRule>();
builder.Services.AddSingleton<IRiskRule, RiskRewardRule>();
builder.Services.AddSingleton<CooldownRule>();
builder.Services.AddSingleton<IRiskRule>(sp => sp.GetRequiredService<CooldownRule>());
builder.Services.AddSingleton<IRiskRule, MaxDrawdownRule>();

builder.Services.AddSingleton<RiskValidationEngine>();

var app = builder.Build();

app.MapGrpcService<RiskGuardGrpcService>();

// ── REST management endpoints (HTTP/1.1 on Health port 5093) ──────────────

app.MapGet("/api/risk/config", (IOptions<RiskSettings> opts) =>
{
    var s = opts.Value;
    return Results.Ok(new
    {
        minRiskReward = s.MinRiskReward,
        maxOrderNotional = s.MaxOrderNotional,
        maxPositionSizePercent = s.MaxPositionSizePercent,
        virtualAccountBalance = s.VirtualAccountBalance,
        maxDrawdownPercent = s.MaxDrawdownPercent,
        cooldownSeconds = s.CooldownSeconds,
        paperTradingOnly = s.PaperTradingOnly,
        allowedSymbols = s.AllowedSymbols
    });
});

app.MapGet("/api/risk/stats", async (
    IOptions<RiskSettings> opts,
    OrderStatsRepository statsRepo,
    ValidationHistory history,
    CooldownRule cooldownRule,
    CancellationToken ct) =>
{
    var s = opts.Value;

    decimal dailyPnl = 0m;
    try { dailyPnl = await statsRepo.GetDailyNetPnlAsync(s.PaperTradingOnly, ct); }
    catch { /* DB unavailable — return zero, never block */ }

    var maxLoss = s.MaxDrawdownPercent / 100m * s.VirtualAccountBalance;
    var drawdownUsedPercent = maxLoss > 0
        ? Math.Min(100m, Math.Round(-dailyPnl / maxLoss * 100m, 1))
        : 0m;

    var (approved, rejected) = history.GetTodayCounts();
    var recent = history.GetRecent();
    var cooldowns = cooldownRule.GetActiveCooldowns();

    return Results.Ok(new
    {
        dailyPnl,
        drawdownUsedPercent,
        maxDrawdownUsd = maxLoss,
        todayApproved = approved,
        todayRejected = rejected,
        cooldowns = cooldowns.Select(c => new
        {
            symbol = c.Symbol,
            lastOrderUtc = c.LastOrderUtc,
            remainingSeconds = c.RemainingSeconds
        }),
        recentValidations = recent.Select(r => new
        {
            symbol = r.Symbol,
            side = r.Side,
            approved = r.Approved,
            rejectionReason = r.RejectionReason,
            timestampUtc = r.TimestampUtc
        })
    });
});

app.MapGet("/api/risk/persistence-status", async Task<IResult> (IRedisPersistenceService redis, CancellationToken ct) =>
{
    if (!await redis.IsAvailableAsync(ct))
    {
        return Results.Json(
            new { status = "degraded", error = "Redis unavailable" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var cooldowns = await redis.LoadAllCooldownsAsync(ct);
    var (approved, rejected) = await redis.LoadTodayCountsAsync(ct);

    return Results.Ok(new
    {
        status = "operational",
        redis = new
        {
            cooldownsStored = cooldowns.Count,
            todayApproved = approved,
            todayRejected = rejected,
            timestamp = DateTime.UtcNow
        }
    });
});

// ── Evaluation history query endpoints ────────────────────────────────────

app.MapGet("/api/risk-evaluations", async (
    [AsParameters] EvaluationQueryParams q,
    IRiskEvaluationRepository repo,
    CancellationToken ct) =>
{
    try
    {
        var (items, total) = await repo.GetPagedAsync(
            q.Symbol, q.Outcome, q.From, q.To, q.SessionId,
            q.Page ?? 1, q.PageSize ?? 20, ct);

        return Results.Ok(new
        {
            items,
            totalCount = total,
            page = q.Page ?? 1,
            pageSize = q.PageSize ?? 20
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to query evaluations: {ex.Message}", statusCode: 500);
    }
});

app.MapGet("/api/risk-evaluations/{evaluationId:guid}", async (
    Guid evaluationId,
    IRiskEvaluationRepository repo,
    CancellationToken ct) =>
{
    try
    {
        var dto = await repo.GetByIdAsync(evaluationId, ct);
        return dto is null
            ? Results.NotFound(new { error = $"Evaluation {evaluationId} not found" })
            : Results.Ok(dto);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to fetch evaluation: {ex.Message}", statusCode: 500);
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "RiskGuard.API" }));
app.MapGet("/", () => "RiskGuard gRPC service is running.");

app.Run();

/// <summary>Query parameters for the evaluation history endpoint.</summary>
record EvaluationQueryParams(
    string? Symbol,
    string? Outcome,
    DateTime? From,
    DateTime? To,
    string? SessionId,
    int? Page,
    int? PageSize);
