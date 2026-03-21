using RiskGuard.API.Services;
using StackExchange.Redis;
using System.Globalization;
using System.Text.Json;

namespace RiskGuard.API.Infrastructure;

public sealed class RedisPersistenceService : IRedisPersistenceService
{
    private const string Prefix = "riskguard";
    private const string CooldownsKey = Prefix + ":cooldowns";
    private const string ValidationsPrefix = Prefix + ":validations:";
    private const string TodayCountsPrefix = Prefix + ":today_counts:";

    private readonly IDatabase _db;
    private readonly ILogger<RedisPersistenceService> _logger;
    private readonly bool _enabled;
    private readonly TimeSpan _baseTtl;
    private readonly TimeSpan _validationTtl;
    private readonly TimeSpan _todayCountsTtl;

    public RedisPersistenceService(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<RedisPersistenceService> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;

        _enabled = TryParseBool(configuration["RISKGUARD_REDIS_ENABLED"], fallback: true);

        var ttlSeconds = TryParseInt(configuration["RISKGUARD_REDIS_TTL_SECONDS"], fallback: 86_400);
        _baseTtl = TimeSpan.FromSeconds(Math.Max(60, ttlSeconds));
        _validationTtl = _baseTtl + TimeSpan.FromMinutes(30);
        _todayCountsTtl = _baseTtl + TimeSpan.FromMinutes(5);
    }

    public async Task SetCooldownAsync(string symbol, DateTime timestampUtc, CancellationToken ct)
    {
        if (!CanUseRedis()) return;

        try
        {
            ct.ThrowIfCancellationRequested();
            var unixSeconds = ToUnixSeconds(timestampUtc);
            await _db.HashSetAsync(CooldownsKey, symbol, unixSeconds).WaitAsync(ct);
            await _db.KeyExpireAsync(CooldownsKey, _baseTtl).WaitAsync(ct);
        }
        catch (Exception ex) when (IsRedisFault(ex))
        {
            _logger.LogWarning(ex, "Failed to persist cooldown for {Symbol}; continuing with in-memory state", symbol);
        }
    }

    public async Task<Dictionary<string, DateTime>> LoadAllCooldownsAsync(CancellationToken ct)
    {
        if (!CanUseRedis()) return [];

        try
        {
            ct.ThrowIfCancellationRequested();
            var entries = await _db.HashGetAllAsync(CooldownsKey).WaitAsync(ct);
            var now = DateTime.UtcNow;
            var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || entry.Value.IsNullOrEmpty)
                    continue;

                if (!long.TryParse((string?)entry.Value, out var unixSeconds))
                    continue;

                var timestampUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
                if (timestampUtc <= now - _baseTtl)
                    continue;

                result[entry.Name!] = timestampUtc;
            }

            return result;
        }
        catch (Exception ex) when (IsRedisFault(ex))
        {
            _logger.LogWarning(ex, "Failed to load cooldown state from Redis; using empty startup state");
            return [];
        }
    }

    public async Task DeleteCooldownAsync(string symbol, CancellationToken ct)
    {
        if (!CanUseRedis()) return;

        try
        {
            ct.ThrowIfCancellationRequested();
            await _db.HashDeleteAsync(CooldownsKey, symbol).WaitAsync(ct);
        }
        catch (Exception ex) when (IsRedisFault(ex))
        {
            _logger.LogWarning(ex, "Failed to delete cooldown for {Symbol}", symbol);
        }
    }

    public async Task AddValidationAsync(ValidationRecord record, CancellationToken ct)
    {
        if (!CanUseRedis()) return;

        try
        {
            ct.ThrowIfCancellationRequested();
            var key = GetValidationKey(record.TimestampUtc);
            var score = ToUnixSeconds(record.TimestampUtc);
            var payload = JsonSerializer.Serialize(record);

            await _db.SortedSetAddAsync(key, payload, score).WaitAsync(ct);
            await _db.KeyExpireAsync(key, _validationTtl).WaitAsync(ct);
        }
        catch (Exception ex) when (IsRedisFault(ex))
        {
            _logger.LogWarning(ex, "Failed to persist validation record for {Symbol}", record.Symbol);
        }
    }

    public async Task<List<ValidationRecord>> LoadTodayValidationsAsync(CancellationToken ct)
    {
        if (!CanUseRedis()) return [];

        try
        {
            ct.ThrowIfCancellationRequested();
            var key = GetValidationKey(DateTime.UtcNow);
            var values = await _db.SortedSetRangeByRankAsync(key, 0, 49, Order.Descending).WaitAsync(ct);
            var result = new List<ValidationRecord>(values.Length);

            foreach (var value in values)
            {
                if (value.IsNullOrEmpty)
                    continue;

                var payload = (string?)value;
                if (string.IsNullOrWhiteSpace(payload))
                    continue;

                var parsed = JsonSerializer.Deserialize<ValidationRecord>(payload);
                if (parsed is not null)
                    result.Add(parsed);
            }

            return result;
        }
        catch (Exception ex) when (IsRedisFault(ex))
        {
            _logger.LogWarning(ex, "Failed to load validation history from Redis; using empty startup state");
            return [];
        }
    }

    public async Task<(int Approved, int Rejected)> LoadTodayCountsAsync(CancellationToken ct)
    {
        if (!CanUseRedis()) return (0, 0);

        try
        {
            ct.ThrowIfCancellationRequested();
            var key = GetTodayCountsKey(DateTime.UtcNow);
            var entries = await _db.HashGetAllAsync(key).WaitAsync(ct);

            if (entries.Length == 0)
                return (0, 0);

            var approved = 0;
            var rejected = 0;

            foreach (var entry in entries)
            {
                if (entry.Name == "approved" && int.TryParse((string?)entry.Value, out var a))
                    approved = a;
                else if (entry.Name == "rejected" && int.TryParse((string?)entry.Value, out var r))
                    rejected = r;
            }

            return (approved, rejected);
        }
        catch (Exception ex) when (IsRedisFault(ex))
        {
            _logger.LogWarning(ex, "Failed to load today's validation counters from Redis; using zeros");
            return (0, 0);
        }
    }

    public async Task UpdateTodayCountsAsync(int approved, int rejected, CancellationToken ct)
    {
        if (!CanUseRedis()) return;

        try
        {
            ct.ThrowIfCancellationRequested();
            var now = DateTime.UtcNow;
            var key = GetTodayCountsKey(now);

            HashEntry[] entries =
            [
                new("approved", approved),
                new("rejected", rejected),
                new("date", DateOnly.FromDateTime(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new("lastUpdated", ToUnixSeconds(now))
            ];

            await _db.HashSetAsync(key, entries).WaitAsync(ct);
            await _db.KeyExpireAsync(key, _todayCountsTtl).WaitAsync(ct);
        }
        catch (Exception ex) when (IsRedisFault(ex))
        {
            _logger.LogWarning(ex, "Failed to update today's validation counters in Redis");
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        if (!CanUseRedis()) return false;

        try
        {
            ct.ThrowIfCancellationRequested();
            _ = await _db.PingAsync().WaitAsync(ct);
            return true;
        }
        catch (Exception ex) when (IsRedisFault(ex))
        {
            _logger.LogWarning(ex, "Redis persistence health check failed");
            return false;
        }
    }

    private static long ToUnixSeconds(DateTime timestampUtc)
        => new DateTimeOffset(DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static bool TryParseBool(string? value, bool fallback)
        => bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int TryParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private bool CanUseRedis()
        => _enabled;

    private static bool IsRedisFault(Exception ex)
        => ex is RedisConnectionException
           or RedisTimeoutException
           or RedisServerException
           or OperationCanceledException
           or TimeoutException
           or JsonException;

    private static string GetValidationKey(DateTime timestampUtc)
        => ValidationsPrefix + DateOnly.FromDateTime(timestampUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string GetTodayCountsKey(DateTime timestampUtc)
        => TodayCountsPrefix + DateOnly.FromDateTime(timestampUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}