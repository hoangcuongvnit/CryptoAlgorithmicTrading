using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace Gateway.API.Settings;

public sealed record TelegramSettingsRecord(
    bool Enabled,
    bool IsConfigured,
    string TokenMasked,
    string ChatIdMasked,
    string? LastTestStatus,
    DateTime? LastTestAtUtc,
    string? LastError,
    string? UpdatedBy,
    DateTime? UpdatedAtUtc);

public sealed class SystemSettingsRepository
{
    private readonly string _connectionString;
    private readonly IDataProtector _protector;
    private readonly ILogger<SystemSettingsRepository> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _isInitialized;

    // Curated IANA timezone IDs supported by the system.
    // Using a whitelist avoids cross-platform OS timezone ID differences.
    private static readonly HashSet<string> KnownTimezones = new(StringComparer.OrdinalIgnoreCase)
    {
        "UTC",
        "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles",
        "America/Anchorage", "America/Honolulu", "America/Toronto", "America/Vancouver",
        "America/Sao_Paulo", "America/Argentina/Buenos_Aires", "America/Mexico_City",
        "America/Bogota", "America/Lima",
        "Europe/London", "Europe/Paris", "Europe/Berlin", "Europe/Madrid", "Europe/Rome",
        "Europe/Amsterdam", "Europe/Brussels", "Europe/Vienna", "Europe/Zurich",
        "Europe/Stockholm", "Europe/Copenhagen", "Europe/Oslo", "Europe/Helsinki",
        "Europe/Warsaw", "Europe/Prague", "Europe/Budapest", "Europe/Athens",
        "Europe/Istanbul", "Europe/Moscow", "Europe/Kiev",
        "Asia/Dubai", "Asia/Riyadh", "Asia/Kolkata", "Asia/Colombo",
        "Asia/Dhaka", "Asia/Yangon", "Asia/Bangkok", "Asia/Ho_Chi_Minh",
        "Asia/Jakarta", "Asia/Singapore", "Asia/Kuala_Lumpur", "Asia/Manila",
        "Asia/Hong_Kong", "Asia/Shanghai", "Asia/Taipei", "Asia/Seoul",
        "Asia/Tokyo", "Asia/Vladivostok",
        "Australia/Perth", "Australia/Darwin", "Australia/Adelaide",
        "Australia/Brisbane", "Australia/Sydney", "Australia/Melbourne",
        "Pacific/Auckland", "Pacific/Fiji", "Pacific/Honolulu",
        "Africa/Cairo", "Africa/Johannesburg", "Africa/Lagos", "Africa/Nairobi",
    };

    public SystemSettingsRepository(
        string connectionString,
        IDataProtector protector,
        ILogger<SystemSettingsRepository> logger)
    {
        _connectionString = connectionString;
        _protector = protector;
        _logger = logger;
    }

    public static IReadOnlyCollection<string> SupportedTimezones => KnownTimezones;

    public bool IsValidTimezone(string timezone) => KnownTimezones.Contains(timezone);

    // ── Timezone ──────────────────────────────────────────────────────────────

    public async Task<string> GetTimezoneAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        const string sql = "SELECT value FROM public.system_settings WHERE key = 'timezone' LIMIT 1;";
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            var value = await conn.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(sql, cancellationToken: ct));
            return value ?? "UTC";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read timezone setting, falling back to UTC");
            return "UTC";
        }
    }

    public async Task UpdateTimezoneAsync(string timezone, string? updatedBy, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        const string sql = """
            INSERT INTO public.system_settings (key, value, updated_at_utc, updated_by)
            VALUES ('timezone', @Value, NOW(), @UpdatedBy)
            ON CONFLICT (key) DO UPDATE
                SET value          = EXCLUDED.value,
                    updated_at_utc = EXCLUDED.updated_at_utc,
                    updated_by     = EXCLUDED.updated_by;
            """;
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Value = timezone, UpdatedBy = updatedBy }, cancellationToken: ct));
        _logger.LogInformation("System timezone updated to {Timezone} by {UpdatedBy}",
            timezone, updatedBy ?? "unknown");
    }

    // ── Telegram ──────────────────────────────────────────────────────────────

    public async Task<TelegramSettingsRecord> GetTelegramSettingsAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        const string sql = """
            SELECT key, value FROM public.system_settings
            WHERE key LIKE 'notifications.telegram.%';
            """;
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            var rows = await conn.QueryAsync<(string Key, string Value)>(
                new CommandDefinition(sql, cancellationToken: ct));

            var d = rows.ToDictionary(r => r.Key, r => r.Value);

            bool enabled = d.TryGetValue("notifications.telegram.enabled", out var ev) && ev == "true";
            bool isConfigured = d.ContainsKey("notifications.telegram.botToken.encrypted");
            string tokenMasked = d.TryGetValue("notifications.telegram.tokenMasked", out var tm) ? tm : "";
            long chatId = d.TryGetValue("notifications.telegram.chatId", out var cv) &&
                          long.TryParse(cv, out var cid) ? cid : 0;
            string chatIdMasked = chatId > 0 ? MaskChatId(chatId) : "";
            string? lastTestStatus = d.TryGetValue("notifications.telegram.lastTestStatus", out var ls) ? ls : null;
            DateTime? lastTestAtUtc = d.TryGetValue("notifications.telegram.lastTestAtUtc", out var lt) &&
                                      DateTime.TryParse(lt, out var ltd) ? ltd : null;
            string? lastError = d.TryGetValue("notifications.telegram.lastError", out var le) && le != "" ? le : null;
            string? updatedBy = d.TryGetValue("notifications.telegram.updatedBy", out var ub) ? ub : null;
            DateTime? updatedAtUtc = d.TryGetValue("notifications.telegram.updatedAtUtc", out var ua) &&
                                     DateTime.TryParse(ua, out var uad) ? uad : null;

            return new TelegramSettingsRecord(
                enabled, isConfigured, tokenMasked, chatIdMasked,
                lastTestStatus, lastTestAtUtc, lastError, updatedBy, updatedAtUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Telegram settings");
            return new TelegramSettingsRecord(false, false, "", "", null, null, null, null, null);
        }
    }

    /// <summary>Returns the decrypted bot token for internal service calls. Never expose in API responses.</summary>
    public async Task<string?> GetDecryptedBotTokenAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = """
            SELECT value FROM public.system_settings
            WHERE key = 'notifications.telegram.botToken.encrypted' LIMIT 1;
            """;
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            var encrypted = await conn.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(sql, cancellationToken: ct));
            return encrypted is null ? null : _protector.Unprotect(encrypted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt bot token");
            return null;
        }
    }

    public async Task<long?> GetChatIdAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = """
            SELECT value FROM public.system_settings
            WHERE key = 'notifications.telegram.chatId' LIMIT 1;
            """;
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            var value = await conn.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(sql, cancellationToken: ct));
            return value is not null && long.TryParse(value, out var id) ? id : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read chatId");
            return null;
        }
    }

    public async Task SaveTelegramSettingsAsync(
        string? plainBotToken,
        long? chatId,
        bool enabled,
        string? updatedBy,
        CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        var now = DateTime.UtcNow;
        var updates = new List<(string Key, string Value)>
        {
            ("notifications.telegram.enabled", enabled ? "true" : "false"),
            ("notifications.telegram.updatedBy", updatedBy ?? "system"),
            ("notifications.telegram.updatedAtUtc", now.ToString("O")),
        };

        if (chatId.HasValue && chatId.Value != 0)
            updates.Add(("notifications.telegram.chatId", chatId.Value.ToString()));

        if (!string.IsNullOrWhiteSpace(plainBotToken))
        {
            updates.Add(("notifications.telegram.botToken.encrypted", _protector.Protect(plainBotToken)));
            updates.Add(("notifications.telegram.tokenMasked", MaskToken(plainBotToken)));
        }

        const string sql = """
            INSERT INTO public.system_settings (key, value, updated_at_utc, updated_by)
            VALUES (@Key, @Value, @Now, @UpdatedBy)
            ON CONFLICT (key) DO UPDATE
                SET value          = EXCLUDED.value,
                    updated_at_utc = EXCLUDED.updated_at_utc,
                    updated_by     = EXCLUDED.updated_by;
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        foreach (var (key, value) in updates)
        {
            await conn.ExecuteAsync(new CommandDefinition(sql,
                new { Key = key, Value = value, Now = now, UpdatedBy = updatedBy }, cancellationToken: ct));
        }

        _logger.LogInformation("Telegram settings saved by {UpdatedBy}", updatedBy ?? "unknown");
    }

    public async Task UpdateTelegramTestResultAsync(bool success, string? error, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        var now = DateTime.UtcNow;
        var updates = new[]
        {
            ("notifications.telegram.lastTestStatus", success ? "success" : "failed"),
            ("notifications.telegram.lastTestAtUtc", now.ToString("O")),
            ("notifications.telegram.lastError", error ?? ""),
        };

        const string sql = """
            INSERT INTO public.system_settings (key, value, updated_at_utc)
            VALUES (@Key, @Value, @Now)
            ON CONFLICT (key) DO UPDATE
                SET value          = EXCLUDED.value,
                    updated_at_utc = EXCLUDED.updated_at_utc;
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        foreach (var (key, value) in updates)
        {
            await conn.ExecuteAsync(new CommandDefinition(sql,
                new { Key = key, Value = value, Now = now }, cancellationToken: ct));
        }
    }

    // ── Schema init ───────────────────────────────────────────────────────────

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            const string sql = """
                CREATE TABLE IF NOT EXISTS public.system_settings (
                    key            TEXT PRIMARY KEY,
                    value          TEXT NOT NULL,
                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    updated_by     TEXT
                );

                INSERT INTO public.system_settings (key, value, updated_at_utc)
                VALUES ('timezone', 'UTC', NOW())
                ON CONFLICT (key) DO NOTHING;
                """;

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Masks a bot token for display. Example: "110201543:AAH...saw"</summary>
    private static string MaskToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return "";
        var colonIdx = token.IndexOf(':');
        if (colonIdx < 0) return "***";
        var prefix = token[..colonIdx];
        var hash = token[(colonIdx + 1)..];
        var maskedHash = hash.Length > 6 ? hash[..3] + "..." + hash[^3..] : "***";
        return $"{prefix}:{maskedHash}";
    }

    private static string MaskChatId(long id)
    {
        var s = id.ToString();
        if (s.Length <= 4) return new string('*', s.Length);
        return s[..2] + new string('*', s.Length - 4) + s[^2..];
    }
}
