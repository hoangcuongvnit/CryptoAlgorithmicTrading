using Microsoft.Extensions.Options;
using Notifier.Worker.Channels;
using Notifier.Worker.Configuration;
using Notifier.Worker.Services;
using Notifier.Worker.Workers;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection("Telegram"));
builder.Services.Configure<RedisSettings>(builder.Configuration.GetSection("Redis"));

// Redis
var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

// Telegram
builder.Services.AddSingleton<TelegramNotifier>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<TelegramSettings>>().Value;
    var logger = sp.GetRequiredService<ILogger<TelegramNotifier>>();
    return new TelegramNotifier(settings.BotToken, settings.ChatId, logger);
});

// Notification history
builder.Services.AddSingleton<NotificationHistory>();

// Workers
builder.Services.AddHostedService<NotifierWorker>();

var app = builder.Build();

// ── REST management endpoints (HTTP/1.1 on port 5094) ─────────────────────

app.MapGet("/api/notifier/config", (
    IOptions<TelegramSettings> telegramOpts,
    IOptions<RedisSettings> redisOpts,
    TelegramNotifier notifier) =>
{
    var t = telegramOpts.Value;
    var maskedChatId = t.ChatId > 0
        ? MaskChatId(t.ChatId)
        : "not configured";

    return Results.Ok(new
    {
        telegramEnabled = notifier.IsEnabled,
        chatId = maskedChatId,
        botConfigured = !string.IsNullOrWhiteSpace(t.BotToken) && t.BotToken != "your_telegram_bot_token_here",
        redisConnection = redisOpts.Value.Connection,
        historyCapacity = 100
    });
});

app.MapGet("/api/notifier/stats", (NotificationHistory history) =>
{
    var (total, byCategory) = history.GetTodayCounts();
    var recent = history.GetRecent();

    return Results.Ok(new
    {
        todayTotal = total,
        todayByCategory = byCategory,
        recentNotifications = recent.Select(r => new
        {
            category = r.Category,
            summary = r.Summary,
            timestampUtc = r.TimestampUtc
        })
    });
});

// POST /api/notifier/validate  — test credentials without saving
app.MapPost("/api/notifier/validate", async (HttpRequest request, CancellationToken ct) =>
{
    var body = await request.ReadFromJsonAsync<TelegramCredentialsRequest>(ct);
    if (body is null || string.IsNullOrWhiteSpace(body.BotToken) || body.ChatId == 0)
        return Results.BadRequest("botToken and chatId are required");

    var (valid, botUsername, message) = await TelegramNotifier.ValidateCredentialsAsync(
        body.BotToken, body.ChatId, ct);

    return Results.Ok(new
    {
        valid,
        botUsername,
        chatReachable = valid,
        message,
    });
});

// POST /api/notifier/reload-config  — hot-swap Telegram credentials
app.MapPost("/api/notifier/reload-config", async (TelegramNotifier notifier, HttpRequest request, CancellationToken ct) =>
{
    var body = await request.ReadFromJsonAsync<TelegramReloadRequest>(ct);
    if (body is null)
        return Results.BadRequest("Request body is required");

    notifier.Reconfigure(body.BotToken ?? "", body.ChatId, body.Enabled);
    return Results.Ok(new { reloaded = true, enabled = notifier.IsEnabled });
});

// POST /api/notifier/test-message  — send a test message using current config
app.MapPost("/api/notifier/test-message", async (TelegramNotifier notifier, HttpRequest request, CancellationToken ct) =>
{
    if (!notifier.IsEnabled)
        return Results.BadRequest(new { success = false, error = "Telegram is not configured or disabled" });

    var body = await request.ReadFromJsonAsync<TestMessageRequest>(ct);
    var message = body?.Message ?? "Test message from Admin UI";

    try
    {
        await notifier.SendDirectMessageAsync(message, ct);
        return Results.Ok(new { success = true, sentAtUtc = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { success = false, error = ex.Message });
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Notifier.Worker" }));

app.Run();

static string MaskChatId(long id)
{
    var s = id.ToString();
    if (s.Length <= 4) return new string('*', s.Length);
    return s[..2] + new string('*', s.Length - 4) + s[^2..];
}

record TelegramCredentialsRequest(string BotToken, long ChatId);
record TelegramReloadRequest(string? BotToken, long ChatId, bool Enabled);
record TestMessageRequest(string? Message);
