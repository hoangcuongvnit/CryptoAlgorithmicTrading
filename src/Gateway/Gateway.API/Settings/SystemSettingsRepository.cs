using Dapper;
using Npgsql;

namespace CryptoAlgorithmicTrading.Gateway.API.Settings;

public sealed class SystemSettingsRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SystemSettingsRepository> _logger;

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

    public SystemSettingsRepository(string connectionString, ILogger<SystemSettingsRepository> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public static IReadOnlyCollection<string> SupportedTimezones => KnownTimezones;

    public bool IsValidTimezone(string timezone) => KnownTimezones.Contains(timezone);

    public async Task<string> GetTimezoneAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT value FROM system_settings WHERE key = 'timezone' LIMIT 1;";
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
        const string sql = """
            INSERT INTO system_settings (key, value, updated_at_utc, updated_by)
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
}
