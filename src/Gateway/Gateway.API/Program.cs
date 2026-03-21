using Gateway.API.Dashboard;
using Gateway.API.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IDashboardQueryService, DashboardQueryService>();
builder.Services.AddSingleton(sp =>
{
    var cs = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");
    var logger = sp.GetRequiredService<ILogger<SystemSettingsRepository>>();
    return new SystemSettingsRepository(cs, logger);
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

var app = builder.Build();

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
    HttpRequest request,
    CancellationToken ct) =>
{
    var body = await request.ReadFromJsonAsync<TimezoneUpdateRequest>(ct);
    if (body is null || string.IsNullOrWhiteSpace(body.Timezone))
        return Results.BadRequest("timezone is required");

    if (!repo.IsValidTimezone(body.Timezone))
        return Results.BadRequest($"Unknown timezone '{body.Timezone}'. Use a supported IANA timezone ID.");

    await repo.UpdateTimezoneAsync(body.Timezone, body.UpdatedBy, ct);
    return Results.Ok(new { timezone = body.Timezone });
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

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Gateway.API", utc = DateTime.UtcNow }));
app.MapFallbackToFile("index.html");

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
