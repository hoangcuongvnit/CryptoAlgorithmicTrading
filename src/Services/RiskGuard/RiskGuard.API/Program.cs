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
builder.Services.AddSingleton<ValidationHistory>();

// Rules — evaluated in this order: fast/cheap checks first, DB-backed checks last
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
        minRiskReward          = s.MinRiskReward,
        maxOrderNotional       = s.MaxOrderNotional,
        maxPositionSizePercent = s.MaxPositionSizePercent,
        virtualAccountBalance  = s.VirtualAccountBalance,
        maxDrawdownPercent     = s.MaxDrawdownPercent,
        cooldownSeconds        = s.CooldownSeconds,
        paperTradingOnly       = s.PaperTradingOnly,
        allowedSymbols         = s.AllowedSymbols
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
    var recent  = history.GetRecent();
    var cooldowns = cooldownRule.GetActiveCooldowns();

    return Results.Ok(new
    {
        dailyPnl,
        drawdownUsedPercent,
        maxDrawdownUsd    = maxLoss,
        todayApproved     = approved,
        todayRejected     = rejected,
        cooldowns         = cooldowns.Select(c => new
        {
            symbol           = c.Symbol,
            lastOrderUtc     = c.LastOrderUtc,
            remainingSeconds = c.RemainingSeconds
        }),
        recentValidations = recent.Select(r => new
        {
            symbol          = r.Symbol,
            side            = r.Side,
            approved        = r.Approved,
            rejectionReason = r.RejectionReason,
            timestampUtc    = r.TimestampUtc
        })
    });
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "RiskGuard.API" }));
app.MapGet("/", () => "RiskGuard gRPC service is running.");

app.Run();
