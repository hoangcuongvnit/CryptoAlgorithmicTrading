namespace Notifier.Worker.Services;

/// <summary>
/// Thread-safe holder for the system-configured IANA timezone.
/// Updated at runtime when the timezone setting changes via Redis pub/sub.
/// </summary>
public sealed class TimezoneService
{
    private volatile TimeZoneInfo _tz = TimeZoneInfo.Utc;
    private volatile string _ianaId = "UTC";

    public string IanaTimezoneId => _ianaId;

    /// <summary>Updates the active timezone. Silently ignores unknown IANA IDs.</summary>
    public void Update(string ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId)) return;
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            _tz = tz;
            _ianaId = ianaId;
        }
        catch (TimeZoneNotFoundException)
        {
            // Keep current timezone — don't crash on unknown IANA ID
        }
    }

    /// <summary>
    /// Converts a UTC datetime to local time and formats it with a UTC offset label.
    /// Example output: "21:32 UTC+7" or "10:15:30 UTC-4" or "14:00 UTC"
    /// </summary>
    public string Format(DateTime utcTime, string fmt = "HH:mm")
    {
        var tz = _tz;
        var utc = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        var offset = tz.GetUtcOffset(utc);

        string offsetLabel;
        if (offset == TimeSpan.Zero)
        {
            offsetLabel = "UTC";
        }
        else
        {
            var sign = offset > TimeSpan.Zero ? "+" : "-";
            var absOffset = offset.Duration();
            offsetLabel = absOffset.Minutes == 0
                ? $"UTC{sign}{(int)absOffset.TotalHours}"
                : $"UTC{sign}{(int)absOffset.TotalHours}:{absOffset.Minutes:D2}";
        }

        return $"{local.ToString(fmt)} {offsetLabel}";
    }
}
