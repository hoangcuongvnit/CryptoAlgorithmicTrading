using System.Text.Json;
using Gateway.API.Dashboard;
using Gateway.API.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IDashboardQueryService, DashboardQueryService>();
builder.Services.AddDataProtection();

builder.Services.AddSingleton(sp =>
{
    var cs = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");
    var protectionProvider = sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
    var protector = protectionProvider.CreateProtector("TelegramBotToken");
    var logger = sp.GetRequiredService<ILogger<SystemSettingsRepository>>();
    return new SystemSettingsRepository(cs, protector, logger);
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

// Push saved Telegram credentials to Notifier on startup so Notifier doesn't need env vars
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromSeconds(3)); // allow Notifier to finish starting
        var logger = app.Services.GetRequiredService<ILogger<SystemSettingsRepository>>();
        try
        {
            var repo = app.Services.GetRequiredService<SystemSettingsRepository>();
            var botToken = await repo.GetDecryptedBotTokenAsync();
            var chatId = await repo.GetChatIdAsync();
            var cfg = await repo.GetTelegramSettingsAsync();
            if (botToken is null || !chatId.HasValue)
            {
                logger.LogInformation("No saved Telegram credentials found — skipping startup sync to Notifier");
                return;
            }

            var factory = app.Services.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient("notifier");
            await client.PostAsJsonAsync("/api/notifier/reload-config",
                new { botToken, chatId = chatId.Value, enabled = cfg.Enabled });
            logger.LogInformation("Telegram config synced to Notifier on startup (enabled={Enabled})", cfg.Enabled);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync Telegram config to Notifier on startup — Notifier will use its own config");
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
