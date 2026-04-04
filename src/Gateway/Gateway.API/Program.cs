using System.Text.Json;
using Gateway.API.Dashboard;
using Gateway.API.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IDashboardQueryService, DashboardQueryService>();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/data-protection-keys"))
    .SetApplicationName("CryptoTradingGateway");

builder.Services.AddSingleton(sp =>
{
    var cs = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");
    var protectionProvider = sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
    var protector = protectionProvider.CreateProtector("TelegramBotToken");
    var binanceProtector = protectionProvider.CreateProtector("BinanceCredentials");
    var logger = sp.GetRequiredService<ILogger<SystemSettingsRepository>>();
    return new SystemSettingsRepository(cs, protector, binanceProtector, logger);
});

builder.Services.AddHttpClient("riskguard", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["RiskGuard:BaseUrl"] ?? "http://localhost:5093");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHttpClient("notifier", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Notifier:BaseUrl"] ?? "http://localhost:5095");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHttpClient("executor", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Executor:BaseUrl"] ?? "http://localhost:5094");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHttpClient("timelinelogger", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["TimelineLogger:BaseUrl"] ?? "http://localhost:5096");
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Redis — used to cache and broadcast system config (e.g., timezone) to other services
var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "redis:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

var app = builder.Build();

// Seed Redis timezone key from PostgreSQL on startup so services read the correct value
// even if they started before the Gateway did.
try
{
    var settingsRepo = app.Services.GetRequiredService<SystemSettingsRepository>();
    var redis = app.Services.GetRequiredService<IConnectionMultiplexer>();
    var seedTz = await settingsRepo.GetTimezoneAsync();
    await redis.GetDatabase().StringSetAsync("system:config:timezone", seedTz);
}
catch (Exception ex)
{
    var seedLogger = app.Services.GetRequiredService<ILogger<Program>>();
    seedLogger.LogWarning(ex, "Could not seed timezone to Redis on startup");
}

app.UseDefaultFiles();
app.UseStaticFiles();

var dashboardGroup = app.MapGroup("/api/dashboard");

dashboardGroup.MapGet("/overview", async (
    [FromServices] IDashboardQueryService service,
    [FromServices] IOptions<DashboardOptions> options,
    [FromQuery] DateTime? startUtc,
    [FromQuery] DateTime? endUtc,
    [FromQuery] string? interval,
    [FromQuery] string[]? symbols,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var resolvedInterval = string.IsNullOrWhiteSpace(interval) ? "1m" : interval.Trim();
    var (resolvedStart, resolvedEnd) = ResolveRange(startUtc, endUtc, resolvedInterval, options.Value);
    var resolvedSymbols = ResolveSymbols(symbols, options.Value);

    var result = await service.GetOverviewAsync(resolvedStart, resolvedEnd, resolvedSymbols, resolvedInterval, cancellationToken);
    SetCacheHeaders(httpContext, options.Value.CacheSeconds);
    return Results.Ok(result);
});

dashboardGroup.MapGet("/candles", async (
    [FromServices] IDashboardQueryService service,
    [FromServices] IOptions<DashboardOptions> options,
    [FromQuery] string? symbol,
    [FromQuery] DateTime? startUtc,
    [FromQuery] DateTime? endUtc,
    [FromQuery] string? interval,
    [FromQuery] string[]? symbols,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var requestedInterval = string.IsNullOrWhiteSpace(interval) ? "1m" : interval.Trim();
    var (resolvedStart, resolvedEnd) = ResolveRange(startUtc, endUtc, requestedInterval, options.Value);
    var resolvedSymbols = ResolveSymbols(symbols, options.Value);
    var resolvedSymbol = string.IsNullOrWhiteSpace(symbol) ? resolvedSymbols.First() : symbol.Trim().ToUpperInvariant();

    // Server-side downsampling: automatically pick appropriate interval based on date range
    var resolvedInterval = PickOptimalInterval(resolvedStart, resolvedEnd, requestedInterval, options.Value);

    var result = await service.GetCandlesAsync(resolvedSymbol, resolvedStart, resolvedEnd, resolvedInterval, resolvedSymbols, cancellationToken);
    SetCacheHeaders(httpContext, options.Value.CacheSeconds);
    return Results.Ok(result);
});

dashboardGroup.MapGet("/quality/coverage", async (
    [FromServices] IDashboardQueryService service,
    [FromServices] IOptions<DashboardOptions> options,
    [FromQuery] DateTime? startUtc,
    [FromQuery] DateTime? endUtc,
    [FromQuery] string? interval,
    [FromQuery] string[]? symbols,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var resolvedInterval = string.IsNullOrWhiteSpace(interval) ? "1m" : interval.Trim();
    var (resolvedStart, resolvedEnd) = ResolveRange(startUtc, endUtc, resolvedInterval, options.Value);
    var resolvedSymbols = ResolveSymbols(symbols, options.Value);

    var result = await service.GetQualityAsync(resolvedStart, resolvedEnd, resolvedSymbols, resolvedInterval, cancellationToken);
    SetCacheHeaders(httpContext, options.Value.CacheSeconds);
    return Results.Ok(result);
});

dashboardGroup.MapGet("/quality/gaps", async (
    [FromServices] IDashboardQueryService service,
    [FromServices] IOptions<DashboardOptions> options,
    [FromQuery] DateTime? startUtc,
    [FromQuery] DateTime? endUtc,
    [FromQuery] string? interval,
    [FromQuery] string[]? symbols,
    [FromQuery] int? page,
    [FromQuery] int? pageSize,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var resolvedInterval = string.IsNullOrWhiteSpace(interval) ? "1m" : interval.Trim();
    var (resolvedStart, resolvedEnd) = ResolveRange(startUtc, endUtc, resolvedInterval, options.Value);
    var resolvedSymbols = ResolveSymbols(symbols, options.Value);
    var resolvedPage = NormalizePage(page, pageSize, options.Value);

    var result = await service.GetGapsAsync(
        resolvedStart,
        resolvedEnd,
        resolvedSymbols,
        resolvedInterval,
        resolvedPage.PageNumber,
        resolvedPage.PageSize,
        cancellationToken);
    SetCacheHeaders(httpContext, options.Value.CacheSeconds);
    return Results.Ok(result);
});

dashboardGroup.MapGet("/schema", async (
    [FromServices] IDashboardQueryService service,
    [FromServices] IOptions<DashboardOptions> options,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var result = await service.GetSchemaAsync(cancellationToken);
    SetCacheHeaders(httpContext, options.Value.CacheSeconds);
    return Results.Ok(result);
});

dashboardGroup.MapGet("/workbench/template/{templateId}", async (
    [FromServices] IDashboardQueryService service,
    [FromServices] IOptions<DashboardOptions> options,
    [FromRoute] string templateId,
    [FromQuery] DateTime? startUtc,
    [FromQuery] DateTime? endUtc,
    [FromQuery] string? interval,
    [FromQuery] string[]? symbols,
    [FromQuery] int? page,
    [FromQuery] int? pageSize,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var resolvedInterval = string.IsNullOrWhiteSpace(interval) ? "1m" : interval.Trim();
    var (resolvedStart, resolvedEnd) = ResolveRange(startUtc, endUtc, resolvedInterval, options.Value);
    var resolvedSymbols = ResolveSymbols(symbols, options.Value);
    var resolvedPage = NormalizePage(page, pageSize, options.Value);

    var result = await service.RunWorkbenchTemplateAsync(
        templateId,
        resolvedStart,
        resolvedEnd,
        resolvedSymbols,
        resolvedInterval,
        resolvedPage.PageNumber,
        resolvedPage.PageSize,
        cancellationToken);
    SetCacheHeaders(httpContext, options.Value.CacheSeconds);
    return Results.Ok(result);
});

// ── Live operational endpoints ───────────────────────────────────────────

app.MapGet("/api/live/orders", async (
    [FromServices] IDashboardQueryService service,
    [FromQuery] int? limit,
    CancellationToken ct) =>
{
    var result = await service.GetRecentOrdersAsync(limit ?? 20, ct);
    return Results.Ok(result);
});

// ── Risk Guard proxy endpoints ────────────────────────────────────────────

var riskGroup = app.MapGroup("/api/risk");

riskGroup.MapGet("/config", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var client = factory.CreateClient("riskguard");
    try
    {
        var response = await client.GetAsync("/api/risk/config", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"RiskGuard unreachable: {ex.Message}", statusCode: 503);
    }
});

riskGroup.MapGet("/stats", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var client = factory.CreateClient("riskguard");
    try
    {
        var response = await client.GetAsync("/api/risk/stats", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"RiskGuard unreachable: {ex.Message}", statusCode: 503);
    }
});

// ── Risk Evaluation history endpoints ────────────────────────────────────

app.MapGet("/api/risk-evaluations", async (
    HttpRequest req, IHttpClientFactory factory, CancellationToken ct) =>
{
    var client = factory.CreateClient("riskguard");
    try
    {
        var qs = req.QueryString.Value ?? string.Empty;
        var response = await client.GetAsync($"/api/risk-evaluations{qs}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"RiskGuard unreachable: {ex.Message}", statusCode: 503);
    }
});

app.MapGet("/api/risk-evaluations/{evaluationId:guid}", async (
    Guid evaluationId, IHttpClientFactory factory, CancellationToken ct) =>
{
    var client = factory.CreateClient("riskguard");
    try
    {
        var response = await client.GetAsync($"/api/risk-evaluations/{evaluationId}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"RiskGuard unreachable: {ex.Message}", statusCode: 503);
    }
});

// ── Symbol Timeline endpoint ──────────────────────────────────────────────

var timelineGroup = app.MapGroup("/api/timeline");

timelineGroup.MapGet("/symbol", async (
    [FromServices] IDashboardQueryService dashService,
    [FromServices] IHttpClientFactory factory,
    [FromQuery] string? symbol,
    [FromQuery] int? minutesBack,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(symbol))
        return Results.BadRequest(new { error = "symbol is required" });

    var validWindows = new[] { 5, 10, 15, 30, 60, 120, 300 };
    var window = minutesBack ?? 60;
    if (!validWindows.Contains(window))
        return Results.BadRequest(new { error = $"minutesBack must be one of: {string.Join(", ", validWindows)}" });

    var toUtc = DateTime.UtcNow;
    var fromUtc = toUtc.AddMinutes(-window);

    var riskClient = factory.CreateClient("riskguard");
    var executorClient = factory.CreateClient("executor");

    var priceSummaryTask = dashService.GetPriceSummaryAsync(symbol, fromUtc, toUtc, ct);

    var riskTask = riskClient.GetAsync(
        $"/api/risk-evaluations?symbol={Uri.EscapeDataString(symbol)}&from={fromUtc:O}&to={toUtc:O}&pageSize=200&page=1", ct);

    var ordersTask = executorClient.GetAsync(
        $"/api/trading/orders?symbol={Uri.EscapeDataString(symbol)}&from={fromUtc:O}&to={toUtc:O}", ct);

    await Task.WhenAll(priceSummaryTask, riskTask, ordersTask);

    var priceSummary = await priceSummaryTask;

    List<TimelineEvent> events = [];

    // Parse risk evaluations
    try
    {
        var riskResponse = await riskTask;
        if (riskResponse.IsSuccessStatusCode)
        {
            var riskJson = await riskResponse.Content.ReadAsStringAsync(ct);
            var riskData = System.Text.Json.JsonDocument.Parse(riskJson);
            if (riskData.RootElement.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var evalId = item.TryGetProperty("evaluationId", out var eid) ? eid.GetString() : null;
                    var outcome = item.TryGetProperty("outcome", out var oc) ? oc.GetString() : "Unknown";
                    var side = item.TryGetProperty("side", out var sd) ? sd.GetString() : null;
                    var reqQty = item.TryGetProperty("requestedQuantity", out var rq) ? rq.GetDecimal() : (decimal?)null;
                    var adjQty = item.TryGetProperty("adjustedQuantity", out var aq) ? aq.GetDecimal() : (decimal?)null;
                    var latency = item.TryGetProperty("evaluationLatencyMs", out var lat) ? lat.GetInt32() : (int?)null;
                    var reason = item.TryGetProperty("finalReasonMessage", out var rm) ? rm.GetString() : null;
                    var evalAt = item.TryGetProperty("evaluatedAtUtc", out var eat) ? eat.GetDateTime() : DateTime.UtcNow;

                    List<object> ruleResults = [];
                    if (item.TryGetProperty("ruleResults", out var rules))
                    {
                        foreach (var rule in rules.EnumerateArray())
                        {
                            ruleResults.Add(new
                            {
                                ruleName = rule.TryGetProperty("ruleName", out var rn) ? rn.GetString() : null,
                                result = rule.TryGetProperty("result", out var res) ? res.GetString() : null,
                                reasonMessage = rule.TryGetProperty("reasonMessage", out var rmsg) ? rmsg.GetString() : null,
                                actualValue = rule.TryGetProperty("actualValue", out var av) ? av.GetString() : null,
                                thresholdValue = rule.TryGetProperty("thresholdValue", out var tv) ? tv.GetString() : null,
                                durationMs = rule.TryGetProperty("durationMs", out var dm) ? dm.GetDouble() : (double?)null,
                                sequenceOrder = rule.TryGetProperty("sequenceOrder", out var so) ? so.GetInt32() : (int?)null,
                            });
                        }
                    }

                    var passedCount = ruleResults.Count(r => ((dynamic)r).result == "Pass");
                    var summary = outcome == "Safe"
                        ? $"Risk evaluation approved — {passedCount}/{ruleResults.Count} rules passed"
                        : $"Risk evaluation {outcome?.ToLower()} — {reason ?? "see details"}";

                    events.Add(new TimelineEvent(
                        evalAt,
                        "RISK_EVALUATION",
                        outcome ?? "Unknown",
                        side,
                        summary,
                        new
                        {
                            evaluationId = evalId,
                            requestedQuantity = reqQty,
                            adjustedQuantity = adjQty,
                            latencyMs = latency,
                            finalReason = reason,
                            ruleResults
                        }));
                }
            }
        }
    }
    catch { /* partial data — skip risk events if service unavailable */ }

    // Parse orders
    try
    {
        var ordersResponse = await ordersTask;
        if (ordersResponse.IsSuccessStatusCode)
        {
            var ordersJson = await ordersResponse.Content.ReadAsStringAsync(ct);
            var ordersData = System.Text.Json.JsonDocument.Parse(ordersJson);
            foreach (var order in ordersData.RootElement.EnumerateArray())
            {
                var orderId = order.TryGetProperty("orderId", out var oid) ? oid.GetString() : null;
                var side = order.TryGetProperty("side", out var sd) ? sd.GetString() : null;
                var qty = order.TryGetProperty("quantity", out var q) ? q.GetDecimal() : (decimal?)null;
                var filledPrice = order.TryGetProperty("filledPrice", out var fp) ? fp.GetDecimal() : (decimal?)null;
                var filledQty = order.TryGetProperty("filledQty", out var fq) ? fq.GetDecimal() : (decimal?)null;
                var sl = order.TryGetProperty("stopLoss", out var slp) ? slp.GetDecimal() : (decimal?)null;
                var tp = order.TryGetProperty("takeProfit", out var tpp) ? tpp.GetDecimal() : (decimal?)null;
                var strategy = order.TryGetProperty("strategy", out var strat) ? strat.GetString() : null;
                var isPaper = order.TryGetProperty("isPaperTrade", out var ip) && ip.GetBoolean();
                var success = order.TryGetProperty("success", out var suc) && suc.GetBoolean();
                var status = order.TryGetProperty("status", out var st) ? st.GetString() : null;
                var errorMsg = order.TryGetProperty("errorMessage", out var em) ? em.GetString() : null;
                var createdAt = order.TryGetProperty("createdAt", out var ca) ? ca.GetDateTime() : DateTime.UtcNow;

                var outcome = success ? "SUCCESS" : "FAILED";
                var summary = success
                    ? $"{side} {qty} {symbol} filled at {filledPrice:F4}"
                    : $"{side} {qty} {symbol} failed — {errorMsg ?? "unknown error"}";

                events.Add(new TimelineEvent(
                    createdAt,
                    "ORDER",
                    outcome,
                    side,
                    summary,
                    new
                    {
                        orderId,
                        quantity = qty,
                        filledPrice,
                        filledQty,
                        stopLoss = sl,
                        takeProfit = tp,
                        strategy,
                        isPaper,
                        status,
                        errorMessage = errorMsg
                    }));
            }
        }
    }
    catch { /* partial data — skip order events if service unavailable */ }

    events.Sort((a, b) => a.TimestampUtc.CompareTo(b.TimestampUtc));

    var riskEvents = events.Where(e => e.EventType == "RISK_EVALUATION").ToList();
    var orderEvents = events.Where(e => e.EventType == "ORDER").ToList();

    var stats = new
    {
        totalEvaluations = riskEvents.Count,
        approvedEvaluations = riskEvents.Count(e => e.Outcome == "Safe"),
        rejectedEvaluations = riskEvents.Count(e => e.Outcome == "Rejected"),
        totalOrders = orderEvents.Count,
        successfulOrders = orderEvents.Count(e => e.Outcome == "SUCCESS"),
        failedOrders = orderEvents.Count(e => e.Outcome == "FAILED"),
        buyOrders = orderEvents.Count(e => string.Equals(e.Side, "Buy", StringComparison.OrdinalIgnoreCase)),
        sellOrders = orderEvents.Count(e => string.Equals(e.Side, "Sell", StringComparison.OrdinalIgnoreCase)),
    };

    return Results.Ok(new
    {
        symbol,
        fromUtc,
        toUtc,
        priceSummary = priceSummary is null ? null : new
        {
            openPrice = priceSummary.OpenPrice,
            highPrice = priceSummary.HighPrice,
            lowPrice = priceSummary.LowPrice,
            closePrice = priceSummary.ClosePrice,
            totalTicks = priceSummary.TotalTicks,
            firstTickUtc = priceSummary.FirstTickUtc,
            lastTickUtc = priceSummary.LastTickUtc,
        },
        events = events.Select(e => new
        {
            timestampUtc = e.TimestampUtc,
            eventType = e.EventType,
            outcome = e.Outcome,
            side = e.Side,
            summary = e.Summary,
            details = e.Details
        }),
        stats
    });
});

// ── TimelineLogger proxy endpoints (MongoDB-backed) ───────────────────────
// Forward /api/timeline/events|summary|dashboard|health to TimelineLogger service

timelineGroup.MapGet("/events", async (
    IHttpClientFactory factory, HttpRequest request, CancellationToken ct) =>
{
    var client = factory.CreateClient("timelinelogger");
    var qs = request.QueryString.Value ?? string.Empty;
    try
    {
        var resp = await client.GetAsync($"/api/timeline/events{qs}", ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)resp.StatusCode);
    }
    catch { return Results.Json(new { status = "unavailable" }, statusCode: 503); }
});

timelineGroup.MapGet("/summary", async (
    IHttpClientFactory factory, HttpRequest request, CancellationToken ct) =>
{
    var client = factory.CreateClient("timelinelogger");
    var qs = request.QueryString.Value ?? string.Empty;
    try
    {
        var resp = await client.GetAsync($"/api/timeline/summary{qs}", ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)resp.StatusCode);
    }
    catch { return Results.Json(new { status = "unavailable" }, statusCode: 503); }
});

timelineGroup.MapGet("/dashboard", async (
    IHttpClientFactory factory, HttpRequest request, CancellationToken ct) =>
{
    var client = factory.CreateClient("timelinelogger");
    var qs = request.QueryString.Value ?? string.Empty;
    try
    {
        var resp = await client.GetAsync($"/api/timeline/dashboard{qs}", ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)resp.StatusCode);
    }
    catch { return Results.Json(new { status = "unavailable" }, statusCode: 503); }
});

timelineGroup.MapGet("/tl-health", async (
    IHttpClientFactory factory, CancellationToken ct) =>
{
    var client = factory.CreateClient("timelinelogger");
    try
    {
        var resp = await client.GetAsync("/api/timeline/health", ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)resp.StatusCode);
    }
    catch { return Results.Json(new { status = "unavailable" }, statusCode: 503); }
});

timelineGroup.MapGet("/export", async (
    IHttpClientFactory factory, HttpRequest request, CancellationToken ct) =>
{
    var client = factory.CreateClient("timelinelogger");
    var qs = request.QueryString.Value ?? string.Empty;
    try
    {
        var resp = await client.GetAsync($"/api/timeline/export{qs}", ct);
        if (!resp.IsSuccessStatusCode)
            return Results.StatusCode((int)resp.StatusCode);
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        var contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var filename = resp.Content.Headers.ContentDisposition?.FileNameStar
            ?? resp.Content.Headers.ContentDisposition?.FileName
            ?? "timeline_export.csv";
        return Results.File(bytes, contentType, filename);
    }
    catch { return Results.Json(new { status = "unavailable" }, statusCode: 503); }
});

// ── Notifier proxy endpoints ──────────────────────────────────────────────

var notifierGroup = app.MapGroup("/api/notifier");

notifierGroup.MapGet("/config", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var client = factory.CreateClient("notifier");
    try
    {
        var response = await client.GetAsync("/api/notifier/config", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Notifier unreachable: {ex.Message}", statusCode: 503);
    }
});

notifierGroup.MapGet("/stats", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var client = factory.CreateClient("notifier");
    try
    {
        var response = await client.GetAsync("/api/notifier/stats", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Notifier unreachable: {ex.Message}", statusCode: 503);
    }
});

notifierGroup.MapGet("/messages", async (
    IHttpClientFactory factory,
    HttpRequest request,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("notifier");
    var qs = request.QueryString.Value ?? string.Empty;

    try
    {
        var response = await client.GetAsync($"/api/notifier/messages{qs}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Notifier unreachable: {ex.Message}", statusCode: 503);
    }
});

// ── Executor trading endpoints proxy ─────────────────────────────────────

var tradingGroup = app.MapGroup("/api/trading");

tradingGroup.MapGet("/stats", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var response = await client.GetAsync("/api/trading/stats", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

tradingGroup.MapGet("/positions", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var response = await client.GetAsync("/api/trading/positions", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

tradingGroup.MapGet("/orders", async (
    IHttpClientFactory factory,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? symbol,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var query = new System.Text.StringBuilder("/api/trading/orders?");
        if (!string.IsNullOrEmpty(symbol)) query.Append($"symbol={Uri.EscapeDataString(symbol)}&");
        if (limit.HasValue) query.Append($"limit={limit.Value}");
        var response = await client.GetAsync(query.ToString().TrimEnd('?', '&'), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

// ── Daily Report proxy endpoints ─────────────────────────────────────────

tradingGroup.MapGet("/report/daily", async (
    IHttpClientFactory factory,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? date,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var url = string.IsNullOrEmpty(date) ? "/api/trading/report/daily" : $"/api/trading/report/daily?date={Uri.EscapeDataString(date)}";
        var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

tradingGroup.MapGet("/report/daily/symbols", async (
    IHttpClientFactory factory,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? date,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var url = string.IsNullOrEmpty(date) ? "/api/trading/report/daily/symbols" : $"/api/trading/report/daily/symbols?date={Uri.EscapeDataString(date)}";
        var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

tradingGroup.MapGet("/report/time-analytics", async (
    IHttpClientFactory factory,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? date,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var url = string.IsNullOrEmpty(date) ? "/api/trading/report/time-analytics" : $"/api/trading/report/time-analytics?date={Uri.EscapeDataString(date)}";
        var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

tradingGroup.MapGet("/report/hourly", async (
    IHttpClientFactory factory,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? date,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var url = string.IsNullOrEmpty(date) ? "/api/trading/report/hourly" : $"/api/trading/report/hourly?date={Uri.EscapeDataString(date)}";
        var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

// ── Session Report proxy endpoints ───────────────────────────────────────

tradingGroup.MapGet("/report/sessions/daily", async (
    IHttpClientFactory factory,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? date,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? mode,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var qs = new System.Text.StringBuilder("/api/trading/report/sessions/daily?");
        if (!string.IsNullOrEmpty(date)) qs.Append($"date={Uri.EscapeDataString(date)}&");
        if (!string.IsNullOrEmpty(mode)) qs.Append($"mode={Uri.EscapeDataString(mode)}&");
        var response = await client.GetAsync(qs.ToString().TrimEnd('?', '&'), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

tradingGroup.MapGet("/report/sessions/equity-curve", async (
    IHttpClientFactory factory,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? from,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? to,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? mode,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var qs = new System.Text.StringBuilder("/api/trading/report/sessions/equity-curve?");
        if (!string.IsNullOrEmpty(from)) qs.Append($"from={Uri.EscapeDataString(from)}&");
        if (!string.IsNullOrEmpty(to)) qs.Append($"to={Uri.EscapeDataString(to)}&");
        if (!string.IsNullOrEmpty(mode)) qs.Append($"mode={Uri.EscapeDataString(mode)}&");
        var response = await client.GetAsync(qs.ToString().TrimEnd('?', '&'), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

tradingGroup.MapGet("/report/sessions/range", async (
    IHttpClientFactory factory,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? from,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? to,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? mode,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var qs = new System.Text.StringBuilder("/api/trading/report/sessions/range?");
        if (!string.IsNullOrEmpty(from)) qs.Append($"from={Uri.EscapeDataString(from)}&");
        if (!string.IsNullOrEmpty(to)) qs.Append($"to={Uri.EscapeDataString(to)}&");
        if (!string.IsNullOrEmpty(mode)) qs.Append($"mode={Uri.EscapeDataString(mode)}&");
        var response = await client.GetAsync(qs.ToString().TrimEnd('?', '&'), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

tradingGroup.MapGet("/report/sessions/{sessionId}/symbols", async (
    IHttpClientFactory factory,
    string sessionId,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? mode,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var url = $"/api/trading/report/sessions/{Uri.EscapeDataString(sessionId)}/symbols";
        if (!string.IsNullOrEmpty(mode)) url += $"?mode={Uri.EscapeDataString(mode)}";
        var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

// ── Budget Management proxy endpoints ─────────────────────────────────────

var budgetGroup = app.MapGroup("/api/trading/budget");

budgetGroup.MapGet("/status", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var response = await client.GetAsync("/api/trading/budget/status", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

budgetGroup.MapGet("/ledger", async (
    IHttpClientFactory factory,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? from,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? to,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? offset,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var qs = new System.Text.StringBuilder("/api/trading/budget/ledger?");
        if (!string.IsNullOrEmpty(from))   qs.Append($"from={Uri.EscapeDataString(from)}&");
        if (!string.IsNullOrEmpty(to))     qs.Append($"to={Uri.EscapeDataString(to)}&");
        if (limit.HasValue)                qs.Append($"limit={limit.Value}&");
        if (offset.HasValue)               qs.Append($"offset={offset.Value}&");
        var response = await client.GetAsync(qs.ToString().TrimEnd('?', '&'), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

budgetGroup.MapPost("/deposit", async (IHttpClientFactory factory, HttpRequest req, CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var body = await req.ReadFromJsonAsync<object>(ct);
        var response = await client.PostAsJsonAsync("/api/trading/budget/deposit", body, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

budgetGroup.MapPost("/withdraw", async (IHttpClientFactory factory, HttpRequest req, CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var body = await req.ReadFromJsonAsync<object>(ct);
        var response = await client.PostAsJsonAsync("/api/trading/budget/withdraw", body, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

budgetGroup.MapPost("/reset", async (IHttpClientFactory factory, HttpRequest req, CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var body = await req.ReadFromJsonAsync<object>(ct);
        var response = await client.PostAsJsonAsync("/api/trading/budget/reset", body, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

budgetGroup.MapGet("/equity-curve", async (
    IHttpClientFactory factory,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? from,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? to,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var qs = new System.Text.StringBuilder("/api/trading/budget/equity-curve?");
        if (!string.IsNullOrEmpty(from)) qs.Append($"from={Uri.EscapeDataString(from)}&");
        if (!string.IsNullOrEmpty(to))   qs.Append($"to={Uri.EscapeDataString(to)}&");
        var response = await client.GetAsync(qs.ToString().TrimEnd('?', '&'), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

// ── Capital Flow proxy ─────────────────────────────────────────────────────

var reportGroup2 = app.MapGroup("/api/trading/report");

reportGroup2.MapGet("/capital-flow", async (
    IHttpClientFactory factory,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? from,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? to,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? mode,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var qs = new System.Text.StringBuilder("/api/trading/report/capital-flow?");
        if (!string.IsNullOrEmpty(from)) qs.Append($"from={Uri.EscapeDataString(from)}&");
        if (!string.IsNullOrEmpty(to))   qs.Append($"to={Uri.EscapeDataString(to)}&");
        if (!string.IsNullOrEmpty(mode)) qs.Append($"mode={Uri.EscapeDataString(mode)}&");
        var response = await client.GetAsync(qs.ToString().TrimEnd('?', '&'), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

// ── System Settings endpoints ──────────────────────────────────────────────

var settingsGroup = app.MapGroup("/api/settings");

settingsGroup.MapGet("/system", async (SystemSettingsRepository repo, CancellationToken ct) =>
{
    var timezone = await repo.GetTimezoneAsync(ct);
    return Results.Ok(new { timezone });
});

settingsGroup.MapGet("/system/timezones", (SystemSettingsRepository repo) =>
    Results.Ok(SystemSettingsRepository.SupportedTimezones));

settingsGroup.MapPut("/system/timezone", async (
    SystemSettingsRepository repo,
    IConnectionMultiplexer redis,
    HttpRequest request,
    CancellationToken ct) =>
{
    var body = await request.ReadFromJsonAsync<TimezoneUpdateRequest>(ct);
    if (body is null || string.IsNullOrWhiteSpace(body.Timezone))
        return Results.BadRequest("timezone is required");

    if (!repo.IsValidTimezone(body.Timezone))
        return Results.BadRequest($"Unknown timezone '{body.Timezone}'. Use a supported IANA timezone ID.");

    await repo.UpdateTimezoneAsync(body.Timezone, body.UpdatedBy, ct);

    // Propagate to Redis so all services pick up the new timezone immediately
    try
    {
        var db = redis.GetDatabase();
        await db.StringSetAsync("system:config:timezone", body.Timezone);
        await redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal("system:config:changed"), body.Timezone);
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Failed to propagate timezone {Timezone} to Redis", body.Timezone);
    }

    return Results.Ok(new { timezone = body.Timezone });
});

// GET /api/settings/notifications/telegram
settingsGroup.MapGet("/notifications/telegram", async (SystemSettingsRepository repo, CancellationToken ct) =>
{
    var cfg = await repo.GetTelegramSettingsAsync(ct);
    return Results.Ok(new
    {
        enabled = cfg.Enabled,
        isConfigured = cfg.IsConfigured,
        tokenMasked = cfg.TokenMasked,
        chatIdMasked = cfg.ChatIdMasked,
        lastTestStatus = cfg.LastTestStatus,
        lastTestAtUtc = cfg.LastTestAtUtc,
        lastError = cfg.LastError,
        updatedBy = cfg.UpdatedBy,
        updatedAtUtc = cfg.UpdatedAtUtc,
    });
});

// POST /api/settings/notifications/telegram/validate
settingsGroup.MapPost("/notifications/telegram/validate", async (
    IHttpClientFactory factory,
    HttpRequest request,
    CancellationToken ct) =>
{
    var body = await request.ReadFromJsonAsync<TelegramValidateRequest>(ct);
    if (body is null || string.IsNullOrWhiteSpace(body.BotToken))
        return Results.BadRequest("botToken is required");
    if (body.ChatId == 0)
        return Results.BadRequest("chatId is required and must be non-zero");

    var client = factory.CreateClient("notifier");
    try
    {
        var response = await client.PostAsJsonAsync("/api/notifier/validate",
            new { botToken = body.BotToken, chatId = body.ChatId }, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Notifier unreachable: {ex.Message}", statusCode: 503);
    }
});

// POST /api/settings/notifications/telegram/validate-saved
settingsGroup.MapPost("/notifications/telegram/validate-saved", async (
    SystemSettingsRepository repo,
    IHttpClientFactory factory,
    CancellationToken ct) =>
{
    var botToken = await repo.GetDecryptedBotTokenAsync(ct);
    var chatId = await repo.GetChatIdAsync(ct);

    if (botToken is null || !chatId.HasValue)
        return Results.BadRequest(new { valid = false, message = "No saved credentials found" });

    var client = factory.CreateClient("notifier");
    try
    {
        var response = await client.PostAsJsonAsync("/api/notifier/validate",
            new { botToken, chatId = chatId.Value }, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Notifier unreachable: {ex.Message}", statusCode: 503);
    }
});

// PUT /api/settings/notifications/telegram
settingsGroup.MapPut("/notifications/telegram", async (
    SystemSettingsRepository repo,
    IHttpClientFactory factory,
    HttpRequest request,
    CancellationToken ct) =>
{
    var body = await request.ReadFromJsonAsync<TelegramSaveRequest>(ct);
    if (body is null)
        return Results.BadRequest("Request body is required");
    if (body.ChatId == 0)
        return Results.BadRequest("chatId is required and must be non-zero");

    var isFirstConfig = !(await repo.GetTelegramSettingsAsync(ct)).IsConfigured;
    if (isFirstConfig && string.IsNullOrWhiteSpace(body.BotToken))
        return Results.BadRequest("botToken is required for first-time configuration");

    if (!string.IsNullOrWhiteSpace(body.BotToken) && body.BotToken.Length < 20)
        return Results.BadRequest("botToken appears to be invalid (too short)");

    await repo.SaveTelegramSettingsAsync(body.BotToken, body.ChatId, body.Enabled, body.UpdatedBy, ct);

    // Trigger Notifier hot-reload with decrypted credentials
    var botToken = await repo.GetDecryptedBotTokenAsync(ct);
    var chatId = await repo.GetChatIdAsync(ct);
    if (botToken is not null && chatId.HasValue)
    {
        var notifierClient = factory.CreateClient("notifier");
        try
        {
            await notifierClient.PostAsJsonAsync("/api/notifier/reload-config",
                new { botToken, chatId = chatId.Value, enabled = body.Enabled }, ct);
        }
        catch (Exception ex)
        {
            // Non-fatal: settings are saved; Notifier will use new config on next restart
            // Log but do not fail the request
            _ = ex; // suppress unused warning
        }
    }

    return Results.Ok(new { saved = true });
});

// POST /api/settings/notifications/telegram/test-message
settingsGroup.MapPost("/notifications/telegram/test-message", async (
    SystemSettingsRepository repo,
    IHttpClientFactory factory,
    HttpRequest request,
    CancellationToken ct) =>
{
    var body = await request.ReadFromJsonAsync<TelegramTestMessageRequest>(ct);
    var message = body?.Message ?? "Test message from Admin UI";

    // Resolve saved credentials and reload Notifier to ensure it has the latest config
    var botToken = await repo.GetDecryptedBotTokenAsync(ct);
    var chatId = await repo.GetChatIdAsync(ct);

    if (botToken is null || !chatId.HasValue)
        return Results.BadRequest(new { success = false, error = "No saved Telegram configuration" });

    var settings = await repo.GetTelegramSettingsAsync(ct);

    var notifierClient = factory.CreateClient("notifier");
    try
    {
        // Ensure Notifier has up-to-date credentials before sending
        await notifierClient.PostAsJsonAsync("/api/notifier/reload-config",
            new { botToken, chatId = chatId.Value, enabled = settings.Enabled }, ct);

        var response = await notifierClient.PostAsJsonAsync("/api/notifier/test-message",
            new { message }, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        var success = response.IsSuccessStatusCode;
        await repo.UpdateTelegramTestResultAsync(success, success ? null : responseBody, ct);

        return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        await repo.UpdateTelegramTestResultAsync(false, ex.Message, ct);
        return Results.Problem($"Notifier unreachable: {ex.Message}", statusCode: 503);
    }
});

// POST /api/settings/notifications/telegram/health-check
settingsGroup.MapPost("/notifications/telegram/health-check", async (
    SystemSettingsRepository repo,
    IHttpClientFactory factory,
    CancellationToken ct) =>
{
    var botToken = await repo.GetDecryptedBotTokenAsync(ct);
    var chatId = await repo.GetChatIdAsync(ct);

    if (botToken is null || !chatId.HasValue)
        return Results.Ok(new
        {
            success = false,
            status = "unhealthy",
            errorCode = "TELEGRAM_CONFIG_MISSING",
            message = "Saved Telegram credentials were not found",
        });

    var client = factory.CreateClient("notifier");
    try
    {
        var response = await client.PostAsJsonAsync("/api/notifier/validate",
            new { botToken, chatId = chatId.Value }, ct);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        var valid = data.TryGetProperty("valid", out var v) && v.GetBoolean();
        var botUsername = data.TryGetProperty("botUsername", out var bu) ? bu.GetString() : null;
        var chatReachable = data.TryGetProperty("chatReachable", out var cr) && cr.GetBoolean();
        var message = data.TryGetProperty("message", out var m) ? m.GetString() : null;

        if (!valid)
        {
            var errorCode = message?.Contains("token", StringComparison.OrdinalIgnoreCase) == true
                ? "TELEGRAM_AUTH_FAILED"
                : "TELEGRAM_UNKNOWN_ERROR";
            return Results.Ok(new
            {
                success = false,
                status = "unhealthy",
                errorCode,
                message = message ?? "Telegram validation failed",
            });
        }

        if (!chatReachable)
        {
            return Results.Ok(new
            {
                success = false,
                status = "unhealthy",
                errorCode = "TELEGRAM_CHAT_UNREACHABLE",
                message = message ?? "Bot is valid but chat is not reachable",
                botUsername,
            });
        }

        await repo.UpdateTelegramTestResultAsync(true, null, ct);

        return Results.Ok(new
        {
            success = true,
            status = "healthy",
            botUsername,
            checkedAtUtc = DateTime.UtcNow,
            details = "Telegram connection is operational",
        });
    }
    catch (HttpRequestException ex)
    {
        await repo.UpdateTelegramTestResultAsync(false, ex.Message, ct);
        return Results.Ok(new
        {
            success = false,
            status = "unhealthy",
            errorCode = "TELEGRAM_NETWORK_ERROR",
            message = $"Notifier unreachable: {ex.Message}",
        });
    }
    catch (Exception ex)
    {
        await repo.UpdateTelegramTestResultAsync(false, ex.Message, ct);
        return Results.Ok(new
        {
            success = false,
            status = "unhealthy",
            errorCode = "TELEGRAM_UNKNOWN_ERROR",
            message = $"Health check failed: {ex.Message}",
        });
    }
});

// ── Exchange (Binance) settings endpoints ─────────────────────────────────

// GET /api/settings/exchange/binance
settingsGroup.MapGet("/exchange/binance", async (SystemSettingsRepository repo, CancellationToken ct) =>
{
    var cfg = await repo.GetExchangeSettingsAsync(ct);
    return Results.Ok(new
    {
        isConfigured = cfg.IsConfigured,
        apiKeyMasked = cfg.ApiKeyMasked,
        apiSecretMasked = cfg.ApiSecretMasked,
        testnetIsConfigured = cfg.TestnetIsConfigured,
        testnetApiKeyMasked = cfg.TestnetApiKeyMasked,
        testnetApiSecretMasked = cfg.TestnetApiSecretMasked,
        useTestnet = cfg.UseTestnet,
        updatedBy = cfg.UpdatedBy,
        updatedAtUtc = cfg.UpdatedAtUtc,
    });
});

// PUT /api/settings/exchange/binance
settingsGroup.MapPut("/exchange/binance", async (
    SystemSettingsRepository repo,
    IHttpClientFactory factory,
    HttpRequest request,
    CancellationToken ct) =>
{
    ExchangeSaveRequest? body;
    try { body = await request.ReadFromJsonAsync<ExchangeSaveRequest>(ct); }
    catch { return Results.BadRequest("Invalid JSON body"); }
    if (body is null) return Results.BadRequest("Request body is required");

    await repo.SaveExchangeSettingsAsync(body.ApiKey, body.ApiSecret,
        body.TestnetApiKey, body.TestnetApiSecret, body.UseTestnet, body.UpdatedBy, ct);

    // Push to Executor — resolve active credentials (use supplied values or fall back to stored ones)
    var apiKey = !string.IsNullOrWhiteSpace(body.ApiKey) ? body.ApiKey : await repo.GetDecryptedApiKeyAsync(ct);
    var apiSecret = !string.IsNullOrWhiteSpace(body.ApiSecret) ? body.ApiSecret : await repo.GetDecryptedApiSecretAsync(ct);
    var testnetApiKey = !string.IsNullOrWhiteSpace(body.TestnetApiKey) ? body.TestnetApiKey : await repo.GetDecryptedTestnetApiKeyAsync(ct);
    var testnetApiSecret = !string.IsNullOrWhiteSpace(body.TestnetApiSecret) ? body.TestnetApiSecret : await repo.GetDecryptedTestnetApiSecretAsync(ct);
    if (apiKey is not null || testnetApiKey is not null)
    {
        try
        {
            var client = factory.CreateClient("executor");
            await client.PostAsJsonAsync("/api/trading/reload-exchange-config",
                new { apiKey = apiKey ?? "", apiSecret = apiSecret ?? "",
                      testnetApiKey = testnetApiKey ?? "", testnetApiSecret = testnetApiSecret ?? "",
                      useTestnet = body.UseTestnet }, ct);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Failed to push exchange config to Executor");
        }
    }

    return Results.Ok(new { saved = true });
});

// POST /api/settings/exchange/binance/validate
settingsGroup.MapPost("/exchange/binance/validate", async (
    IHttpClientFactory factory,
    HttpRequest request,
    CancellationToken ct) =>
{
    ExchangeValidateRequest? body;
    try { body = await request.ReadFromJsonAsync<ExchangeValidateRequest>(ct); }
    catch { return Results.BadRequest("Invalid JSON body"); }
    if (body is null || string.IsNullOrWhiteSpace(body.ApiKey) || string.IsNullOrWhiteSpace(body.ApiSecret))
        return Results.BadRequest("apiKey and apiSecret are required");

    try
    {
        var client = factory.CreateClient("executor");
        var response = await client.PostAsJsonAsync("/api/trading/validate-exchange",
            new { body.ApiKey, body.ApiSecret, body.UseTestnet }, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Ok(new { valid = false, message = $"Executor unreachable: {ex.Message}" });
    }
});

// POST /api/settings/exchange/binance/validate-saved
// Body: { useTestnet: bool } — picks live or testnet keys from DB accordingly
settingsGroup.MapPost("/exchange/binance/validate-saved", async (
    SystemSettingsRepository repo,
    IHttpClientFactory factory,
    HttpRequest request,
    CancellationToken ct) =>
{
    ValidateSavedRequest? body;
    try { body = await request.ReadFromJsonAsync<ValidateSavedRequest>(ct); }
    catch { body = null; }

    // Default to the currently active environment stored in DB when no body supplied
    var cfg = await repo.GetExchangeSettingsAsync(ct);
    var useTestnet = body?.UseTestnet ?? cfg.UseTestnet;

    string? apiKey, apiSecret;
    if (useTestnet)
    {
        apiKey = await repo.GetDecryptedTestnetApiKeyAsync(ct);
        apiSecret = await repo.GetDecryptedTestnetApiSecretAsync(ct);
        if (apiKey is null || apiSecret is null)
            return Results.Ok(new { valid = false, message = "No saved Testnet credentials found" });
    }
    else
    {
        apiKey = await repo.GetDecryptedApiKeyAsync(ct);
        apiSecret = await repo.GetDecryptedApiSecretAsync(ct);
        if (apiKey is null || apiSecret is null)
            return Results.Ok(new { valid = false, message = "No saved Live credentials found" });
    }

    try
    {
        var client = factory.CreateClient("executor");
        var response = await client.PostAsJsonAsync("/api/trading/validate-exchange",
            new { apiKey, apiSecret, useTestnet }, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Ok(new { valid = false, message = $"Executor unreachable: {ex.Message}" });
    }
});

// ── Trading Mode settings endpoints ───────────────────────────────────────

// GET /api/settings/trading/mode
settingsGroup.MapGet("/trading/mode", async (SystemSettingsRepository repo, CancellationToken ct) =>
{
    var cfg = await repo.GetTradingModeSettingsAsync(ct);
    return Results.Ok(new
    {
        paperTradingMode = cfg.PaperTradingMode,
        initialBalance = cfg.InitialBalance,
        updatedBy = cfg.UpdatedBy,
        updatedAtUtc = cfg.UpdatedAtUtc,
    });
});

// PUT /api/settings/trading/mode
settingsGroup.MapPut("/trading/mode", async (
    SystemSettingsRepository repo,
    IHttpClientFactory factory,
    HttpRequest request,
    CancellationToken ct) =>
{
    TradingModeSaveRequest? body;
    try { body = await request.ReadFromJsonAsync<TradingModeSaveRequest>(ct); }
    catch { return Results.BadRequest("Invalid JSON body"); }
    if (body is null) return Results.BadRequest("Request body is required");

    await repo.SaveTradingModeSettingsAsync(body.PaperTradingMode, body.InitialBalance, body.UpdatedBy, ct);

    // Push to Executor
    try
    {
        var client = factory.CreateClient("executor");
        await client.PostAsJsonAsync("/api/trading/reload-trading-config",
            new { paperTradingMode = body.PaperTradingMode, initialBalance = body.InitialBalance }, ct);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to push trading mode to Executor");
    }

    // Push PaperTradingOnly to RiskGuard
    try
    {
        var client = factory.CreateClient("riskguard");
        await client.PostAsJsonAsync("/api/risk/reload-config",
            new { paperTradingOnly = body.PaperTradingMode }, ct);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to push trading mode to RiskGuard");
    }

    return Results.Ok(new { saved = true });
});

// ── Risk Management settings endpoints ────────────────────────────────────

// GET /api/settings/risk
settingsGroup.MapGet("/risk", async (SystemSettingsRepository repo, CancellationToken ct) =>
{
    var cfg = await repo.GetRiskSettingsAsync(ct);
    return Results.Ok(new
    {
        maxDrawdownPercent = cfg.MaxDrawdownPercent,
        minRiskReward = cfg.MinRiskReward,
        maxPositionSizePercent = cfg.MaxPositionSizePercent,
        cooldownSeconds = cfg.CooldownSeconds,
        updatedBy = cfg.UpdatedBy,
        updatedAtUtc = cfg.UpdatedAtUtc,
    });
});

// PUT /api/settings/risk
settingsGroup.MapPut("/risk", async (
    SystemSettingsRepository repo,
    IHttpClientFactory factory,
    HttpRequest request,
    CancellationToken ct) =>
{
    RiskSaveRequest? body;
    try { body = await request.ReadFromJsonAsync<RiskSaveRequest>(ct); }
    catch { return Results.BadRequest("Invalid JSON body"); }
    if (body is null) return Results.BadRequest("Request body is required");

    if (body.MaxDrawdownPercent is < 0.1m or > 100m) return Results.BadRequest("maxDrawdownPercent must be between 0.1 and 100");
    if (body.MinRiskReward is < 0.5m or > 10m) return Results.BadRequest("minRiskReward must be between 0.5 and 10");
    if (body.MaxPositionSizePercent is < 0.1m or > 100m) return Results.BadRequest("maxPositionSizePercent must be between 0.1 and 100");
    if (body.CooldownSeconds is < 0 or > 3600) return Results.BadRequest("cooldownSeconds must be between 0 and 3600");

    await repo.SaveRiskSettingsAsync(
        body.MaxDrawdownPercent, body.MinRiskReward,
        body.MaxPositionSizePercent, body.CooldownSeconds,
        body.UpdatedBy, ct);

    // Push to RiskGuard
    try
    {
        var client = factory.CreateClient("riskguard");
        await client.PostAsJsonAsync("/api/risk/reload-config", new
        {
            maxDrawdownPercent = body.MaxDrawdownPercent,
            minRiskReward = body.MinRiskReward,
            maxPositionSizePercent = body.MaxPositionSizePercent,
            cooldownSeconds = body.CooldownSeconds,
        }, ct);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to push risk config to RiskGuard");
    }

    return Results.Ok(new { saved = true });
});

// ── Order Amount Limit settings endpoints ────────────────────────────────

// GET /api/settings/order-amount-limit
settingsGroup.MapGet("/order-amount-limit", async (SystemSettingsRepository repo, CancellationToken ct) =>
{
    var cfg = await repo.GetOrderAmountLimitAsync(ct);
    return Results.Ok(new
    {
        minOrderAmount = cfg.MinOrderAmount,
        maxOrderAmount = cfg.MaxOrderAmount,
        updatedBy = cfg.UpdatedBy,
        updatedAtUtc = cfg.UpdatedAtUtc,
    });
});

// PUT /api/settings/order-amount-limit
settingsGroup.MapPut("/order-amount-limit", async (
    SystemSettingsRepository repo,
    IHttpClientFactory factory,
    HttpRequest request,
    CancellationToken ct) =>
{
    OrderAmountLimitSaveRequest? body;
    try { body = await request.ReadFromJsonAsync<OrderAmountLimitSaveRequest>(ct); }
    catch { return Results.BadRequest("Invalid JSON body"); }
    if (body is null) return Results.BadRequest("Request body is required");
    if (body.MinOrderAmount is null) return Results.BadRequest("minOrderAmount is required");
    if (body.MaxOrderAmount is null) return Results.BadRequest("maxOrderAmount is required");
    if (body.MinOrderAmount <= 0 || body.MaxOrderAmount <= 0)
        return Results.BadRequest("minOrderAmount and maxOrderAmount must be greater than 0");
    if (body.MinOrderAmount > body.MaxOrderAmount)
        return Results.BadRequest("minOrderAmount must be less than or equal to maxOrderAmount");

    await repo.SaveOrderAmountLimitAsync(
        body.MinOrderAmount.Value,
        body.MaxOrderAmount.Value,
        body.UpdatedBy,
        ct);

    try
    {
        var client = factory.CreateClient("executor");
        await client.PostAsJsonAsync("/api/trading/reload-order-amount-limit", new
        {
            minOrderAmount = body.MinOrderAmount,
            maxOrderAmount = body.MaxOrderAmount,
        }, ct);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to push order amount limits to Executor");
    }

    return Results.Ok(new { saved = true });
});

// ── HouseKeeper settings endpoints ────────────────────────────────────────

// GET /api/settings/housekeeper
settingsGroup.MapGet("/housekeeper", async (SystemSettingsRepository repo, CancellationToken ct) =>
{
    var cfg = await repo.GetHouseKeeperSettingsAsync(ct);
    return Results.Ok(new
    {
        enabled = cfg.Enabled,
        dryRun = cfg.DryRun,
        scheduleUtc = cfg.ScheduleUtc,
        retentionOrdersDays = cfg.RetentionOrdersDays,
        retentionGapsDays = cfg.RetentionGapsDays,
        retentionTicksMonths = cfg.RetentionTicksMonths,
        batchSize = cfg.BatchSize,
        maxRunSeconds = cfg.MaxRunSeconds,
        updatedBy = cfg.UpdatedBy,
        updatedAtUtc = cfg.UpdatedAtUtc,
    });
});

// PUT /api/settings/housekeeper
settingsGroup.MapPut("/housekeeper", async (
    SystemSettingsRepository repo,
    HttpRequest request,
    CancellationToken ct) =>
{
    HouseKeeperSaveRequest? body;
    try { body = await request.ReadFromJsonAsync<HouseKeeperSaveRequest>(ct); }
    catch { return Results.BadRequest("Invalid JSON body"); }
    if (body is null) return Results.BadRequest("Request body is required");

    await repo.SaveHouseKeeperSettingsAsync(
        body.Enabled, body.DryRun, body.ScheduleUtc,
        body.RetentionOrdersDays, body.RetentionGapsDays, body.RetentionTicksMonths,
        body.BatchSize, body.MaxRunSeconds, body.UpdatedBy, ct);

    return Results.Ok(new { saved = true, note = "HouseKeeper service restart required for changes to take effect" });
});

// ── Trading Control proxy endpoints ──────────────────────────────────────

var controlGroup = app.MapGroup("/api/control");

controlGroup.MapPost("/close-all", async (IHttpClientFactory factory, HttpRequest req, CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var body = await req.ReadFromJsonAsync<object>(ct);
        var response = await client.PostAsJsonAsync("/api/trading/control/close-all", body, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

controlGroup.MapPost("/close-all/schedule", async (IHttpClientFactory factory, HttpRequest req, CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var body = await req.ReadFromJsonAsync<object>(ct);
        var response = await client.PostAsJsonAsync("/api/trading/control/close-all/schedule", body, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

controlGroup.MapPost("/close-all/cancel", async (IHttpClientFactory factory, HttpRequest req, CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var body = await req.ReadFromJsonAsync<object>(ct);
        var response = await client.PostAsJsonAsync("/api/trading/control/close-all/cancel", body, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

controlGroup.MapGet("/close-all/status", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var response = await client.GetAsync("/api/trading/control/close-all/status", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

controlGroup.MapGet("/close-all/history", async (
    IHttpClientFactory factory,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var url = limit.HasValue
            ? $"/api/trading/control/close-all/history?limit={limit.Value}"
            : "/api/trading/control/close-all/history";
        var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

controlGroup.MapPost("/trading/resume", async (IHttpClientFactory factory, HttpRequest req, CancellationToken ct) =>
{
    var client = factory.CreateClient("executor");
    try
    {
        var body = await req.ReadFromJsonAsync<object>(ct);
        var response = await client.PostAsJsonAsync("/api/trading/control/resume", body, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Executor unreachable: {ex.Message}", statusCode: 503);
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Gateway.API", utc = DateTime.UtcNow }));
app.MapFallbackToFile("index.html");

// Push all saved settings to services on startup (DB is source of truth after first UI save)
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromSeconds(3)); // allow services to finish starting
        var logger = app.Services.GetRequiredService<ILogger<SystemSettingsRepository>>();
        var repo = app.Services.GetRequiredService<SystemSettingsRepository>();
        var factory = app.Services.GetRequiredService<IHttpClientFactory>();

        // Sync Telegram → Notifier
        try
        {
            var botToken = await repo.GetDecryptedBotTokenAsync();
            var chatId = await repo.GetChatIdAsync();
            var telegramCfg = await repo.GetTelegramSettingsAsync();
            if (botToken is not null && chatId.HasValue)
            {
                var client = factory.CreateClient("notifier");
                await client.PostAsJsonAsync("/api/notifier/reload-config",
                    new { botToken, chatId = chatId.Value, enabled = telegramCfg.Enabled });
                logger.LogInformation("Telegram config synced to Notifier on startup (enabled={Enabled})", telegramCfg.Enabled);
            }
            else
            {
                logger.LogInformation("No saved Telegram credentials — skipping startup sync to Notifier");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync Telegram config to Notifier on startup");
        }

        // Sync Exchange credentials → Executor
        try
        {
            var apiKey = await repo.GetDecryptedApiKeyAsync();
            var apiSecret = await repo.GetDecryptedApiSecretAsync();
            var testnetApiKey = await repo.GetDecryptedTestnetApiKeyAsync();
            var testnetApiSecret = await repo.GetDecryptedTestnetApiSecretAsync();
            var exchangeCfg = await repo.GetExchangeSettingsAsync();
            if (apiKey is not null || testnetApiKey is not null)
            {
                var client = factory.CreateClient("executor");
                await client.PostAsJsonAsync("/api/trading/reload-exchange-config",
                    new { apiKey = apiKey ?? "", apiSecret = apiSecret ?? "",
                          testnetApiKey = testnetApiKey ?? "", testnetApiSecret = testnetApiSecret ?? "",
                          useTestnet = exchangeCfg.UseTestnet });
                logger.LogInformation("Exchange credentials synced to Executor on startup (testnet={UseTestnet})", exchangeCfg.UseTestnet);
            }
            else
            {
                logger.LogInformation("No saved Binance credentials — skipping startup sync to Executor");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync Exchange credentials to Executor on startup");
        }

        // Sync Trading Mode → Executor + RiskGuard
        try
        {
            var tradingCfg = await repo.GetTradingModeSettingsAsync();
            var executorClient = factory.CreateClient("executor");
            await executorClient.PostAsJsonAsync("/api/trading/reload-trading-config",
                new { paperTradingMode = tradingCfg.PaperTradingMode, initialBalance = tradingCfg.InitialBalance });

            var riskClient = factory.CreateClient("riskguard");
            await riskClient.PostAsJsonAsync("/api/risk/reload-config",
                new { paperTradingOnly = tradingCfg.PaperTradingMode });

            logger.LogInformation("Trading mode synced on startup (paper={PaperTradingMode})", tradingCfg.PaperTradingMode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync Trading Mode on startup");
        }

        // Sync Order Amount Limits → Executor
        try
        {
            var orderAmountCfg = await repo.GetOrderAmountLimitAsync();
            var client = factory.CreateClient("executor");
            await client.PostAsJsonAsync("/api/trading/reload-order-amount-limit", new
            {
                minOrderAmount = orderAmountCfg.MinOrderAmount,
                maxOrderAmount = orderAmountCfg.MaxOrderAmount,
            });
            logger.LogInformation("Order amount limits synced to Executor on startup");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync Order Amount Limits to Executor on startup");
        }

        // Sync Risk settings → RiskGuard
        try
        {
            var riskCfg = await repo.GetRiskSettingsAsync();
            var client = factory.CreateClient("riskguard");
            await client.PostAsJsonAsync("/api/risk/reload-config", new
            {
                maxDrawdownPercent = riskCfg.MaxDrawdownPercent,
                minRiskReward = riskCfg.MinRiskReward,
                maxPositionSizePercent = riskCfg.MaxPositionSizePercent,
                cooldownSeconds = riskCfg.CooldownSeconds,
            });
            logger.LogInformation("Risk settings synced to RiskGuard on startup");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync Risk settings to RiskGuard on startup");
        }
    });
});

app.Run();

static (DateTime StartUtc, DateTime EndUtc) ResolveRange(DateTime? startUtc, DateTime? endUtc, string interval, DashboardOptions options)
{
    var resolvedEnd = (endUtc ?? DateTime.UtcNow).ToUniversalTime();
    var resolvedStart = (startUtc ?? resolvedEnd.AddDays(-7)).ToUniversalTime();

    if (resolvedStart >= resolvedEnd)
    {
        resolvedStart = resolvedEnd.AddDays(-1);
    }

    var maxRangeDays = options.ResolveMaxRangeDays(interval);
    if ((resolvedEnd - resolvedStart).TotalDays > maxRangeDays)
    {
        resolvedStart = resolvedEnd.AddDays(-maxRangeDays);
    }

    return (resolvedStart, resolvedEnd);
}

static string[] ResolveSymbols(string[]? symbols, DashboardOptions options)
{
    if (symbols is { Length: > 0 })
    {
        return symbols
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    return options.DefaultSymbols.Length > 0
        ? options.DefaultSymbols
        : ["BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT"];
}

static (int PageNumber, int PageSize) NormalizePage(int? page, int? pageSize, DashboardOptions options)
{
    var resolvedPageNumber = page.GetValueOrDefault(1);
    if (resolvedPageNumber <= 0)
    {
        resolvedPageNumber = 1;
    }

    var resolvedPageSize = pageSize.GetValueOrDefault(options.DefaultPageSize);
    if (resolvedPageSize <= 0)
    {
        resolvedPageSize = options.DefaultPageSize;
    }

    resolvedPageSize = Math.Min(resolvedPageSize, options.MaxPageSize);

    return (resolvedPageNumber, resolvedPageSize);
}

static string PickOptimalInterval(DateTime startUtc, DateTime endUtc, string requestedInterval, DashboardOptions options)
{
    // Server-side downsampling: automatically aggregate to higher intervals for large ranges
    // to keep chart rendering performant and stay under MaxCandlesPerRequest
    var rangeDays = (endUtc - startUtc).TotalDays;

    // If user explicitly requested a higher interval, honor it
    if (!string.Equals(requestedInterval, "1m", StringComparison.OrdinalIgnoreCase))
    {
        return requestedInterval;
    }

    // For 1m requests, downsample based on date range:
    // - >7 days: use 5m (1440 * 7 / 5 = ~2000 candles)
    // - >30 days: use 15m (1440 * 30 / 15 = ~2880 candles)
    // - >90 days: use 1h (24 * 90 = ~2160 candles)
    // - >365 days: use 1d
    if (rangeDays > 365)
    {
        return "1d";
    }
    if (rangeDays > 90)
    {
        return "1h";
    }
    if (rangeDays > 30)
    {
        return "15m";
    }
    if (rangeDays > 7)
    {
        return "5m";
    }

    return "1m";
}

static void SetCacheHeaders(HttpContext context, int cacheSeconds)
{
    // Set HTTP cache headers to allow browser/CDN caching for dashboard endpoints
    // This reduces server load and improves perceived performance
    context.Response.Headers.CacheControl = $"public, max-age={cacheSeconds}";
    context.Response.Headers.Expires = DateTime.UtcNow.AddSeconds(cacheSeconds).ToString("R");
}

record TimezoneUpdateRequest(string Timezone, string? UpdatedBy);
record TelegramValidateRequest(string BotToken, long ChatId);
record TelegramSaveRequest(bool Enabled, string? BotToken, long ChatId, string? UpdatedBy);
record TelegramTestMessageRequest(string? Message);
record ExchangeSaveRequest(string? ApiKey, string? ApiSecret, string? TestnetApiKey, string? TestnetApiSecret, bool UseTestnet, string? UpdatedBy);
record ExchangeValidateRequest(string ApiKey, string ApiSecret, bool UseTestnet);
record ValidateSavedRequest(bool UseTestnet);
record OrderAmountLimitSaveRequest(decimal? MinOrderAmount, decimal? MaxOrderAmount, string? UpdatedBy);
record TradingModeSaveRequest(bool PaperTradingMode, decimal InitialBalance, string? UpdatedBy);
record RiskSaveRequest(decimal MaxDrawdownPercent, decimal MinRiskReward, decimal MaxPositionSizePercent, int CooldownSeconds, string? UpdatedBy);
record HouseKeeperSaveRequest(bool Enabled, bool DryRun, string ScheduleUtc, int RetentionOrdersDays, int RetentionGapsDays, int RetentionTicksMonths, int BatchSize, int MaxRunSeconds, string? UpdatedBy);
record TimelineEvent(DateTime TimestampUtc, string EventType, string Outcome, string? Side, string Summary, object Details);
