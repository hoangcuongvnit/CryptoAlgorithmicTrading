using Dapper;
using Npgsql;

namespace Notifier.Worker.Infrastructure;

public sealed class NotificationMessageRepository
{
    private readonly string? _connectionString;
    private readonly ILogger<NotificationMessageRepository> _logger;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_connectionString);

    public NotificationMessageRepository(IConfiguration configuration, ILogger<NotificationMessageRepository> logger)
    {
        _logger = logger;
        _connectionString = configuration.GetConnectionString("Postgres");

        if (!IsEnabled)
            _logger.LogWarning("Notification DB persistence disabled: ConnectionStrings:Postgres is not configured.");
    }

    public async Task SaveSentAsync(string category, string message, string? externalMessageId, CancellationToken ct = default)
    {
        if (!IsEnabled) return;

        const string sql = """
            INSERT INTO public.notifier_message_logs (
                channel, category, summary, message_text, sent_at_utc, external_message_id
            ) VALUES (
                'telegram', @Category, @Summary, @MessageText, @SentAtUtc, @ExternalMessageId
            );
            """;

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                Category = category,
                Summary = BuildSummary(message),
                MessageText = message,
                SentAtUtc = DateTime.UtcNow,
                ExternalMessageId = externalMessageId,
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist sent Telegram message. Category={Category}", category);
        }
    }

    public async Task<(int Total, IReadOnlyDictionary<string, int> ByCategory)> GetTodayCountsAsync(CancellationToken ct = default)
    {
        if (!IsEnabled)
            return (0, new Dictionary<string, int>());

        var dayStartUtc = DateTime.UtcNow.Date;

        const string totalSql = """
            SELECT COUNT(*)
            FROM public.notifier_message_logs
            WHERE sent_at_utc >= @DayStartUtc;
            """;

        const string byCategorySql = """
            SELECT category AS "Category", COUNT(*) AS "Count"
            FROM public.notifier_message_logs
            WHERE sent_at_utc >= @DayStartUtc
            GROUP BY category;
            """;

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                totalSql,
                new { DayStartUtc = dayStartUtc },
                cancellationToken: ct));

            var grouped = (await conn.QueryAsync<CategoryCountRow>(new CommandDefinition(
                byCategorySql,
                new { DayStartUtc = dayStartUtc },
                cancellationToken: ct))).ToList();

            return (total, grouped.ToDictionary(x => x.Category, x => x.Count, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query today's notifier message counts.");
            return (0, new Dictionary<string, int>());
        }
    }

    public async Task<IReadOnlyList<NotificationMessageRow>> GetRecentAsync(int limit, CancellationToken ct = default)
    {
        if (!IsEnabled)
            return [];

        var clampedLimit = Math.Clamp(limit, 1, 200);

        const string sql = """
            SELECT
                id AS "Id",
                category AS "Category",
                summary AS "Summary",
                message_text AS "MessageText",
                sent_at_utc AS "TimestampUtc",
                external_message_id AS "ExternalMessageId"
            FROM public.notifier_message_logs
            ORDER BY sent_at_utc DESC
            LIMIT @Limit;
            """;

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            var rows = await conn.QueryAsync<NotificationMessageRow>(new CommandDefinition(
                sql,
                new { Limit = clampedLimit },
                cancellationToken: ct));

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query recent notifier messages.");
            return [];
        }
    }

    private static string BuildSummary(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "(empty message)";

        var normalized = message.Replace("\r", string.Empty).Trim();
        var firstLine = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? normalized;

        return firstLine.Length <= 220
            ? firstLine
            : firstLine[..217] + "...";
    }

    private sealed class CategoryCountRow
    {
        public string Category { get; init; } = string.Empty;
        public int Count { get; init; }
    }
}

public sealed class NotificationMessageRow
{
    public long Id { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string MessageText { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; }
    public string? ExternalMessageId { get; init; }
}
