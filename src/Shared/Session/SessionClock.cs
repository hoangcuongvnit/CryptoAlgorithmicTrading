using CryptoTrading.Shared.DTOs;
using Microsoft.Extensions.Options;

namespace CryptoTrading.Shared.Session;

public sealed class SessionClock
{
    private readonly SessionSettings _settings;
    private readonly TimeZoneInfo _timeZone;

    public SessionClock(IOptions<SessionSettings> settings)
    {
        _settings = settings.Value;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(_settings.SessionTimeZone);
    }

    public SessionInfo GetCurrentSession() => GetSession(DateTimeOffset.UtcNow);

    public SessionInfo GetSession(DateTimeOffset utcNow)
    {
        var sessionTzTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow.UtcDateTime, _timeZone);

        var sessionNumber = (sessionTzTime.Hour / _settings.SessionHours) + 1;

        var sessionStartHour = (sessionNumber - 1) * _settings.SessionHours;
        var sessionStartLocal = new DateTime(
            sessionTzTime.Year, sessionTzTime.Month, sessionTzTime.Day,
            sessionStartHour, 0, 0, DateTimeKind.Unspecified);
        var sessionEndLocal = sessionStartLocal.AddHours(_settings.SessionHours);

        var sessionStartUtc = TimeZoneInfo.ConvertTimeToUtc(sessionStartLocal, _timeZone);
        var sessionEndUtc = TimeZoneInfo.ConvertTimeToUtc(sessionEndLocal, _timeZone);

        var liquidationStartUtc = sessionEndUtc.AddMinutes(-_settings.LiquidationWindowMinutes);
        var softUnwindStartUtc = liquidationStartUtc.AddMinutes(-_settings.SoftUnwindMinutes);
        var forcedFlattenStartUtc = sessionEndUtc.AddMinutes(-_settings.ForcedFlattenMinutes);

        var now = utcNow.UtcDateTime;
        var timeToEnd = sessionEndUtc - now;
        var timeToLiquidation = liquidationStartUtc - now;

        var phase = DeterminePhase(now, sessionStartUtc, softUnwindStartUtc,
            liquidationStartUtc, forcedFlattenStartUtc, sessionEndUtc);

        var sessionId = ComputeSessionId(sessionTzTime, sessionNumber);

        return new SessionInfo(
            SessionId: sessionId,
            SessionNumber: sessionNumber,
            SessionStartUtc: sessionStartUtc,
            SessionEndUtc: sessionEndUtc,
            LiquidationStartUtc: liquidationStartUtc,
            CurrentPhase: phase,
            TimeToEnd: timeToEnd > TimeSpan.Zero ? timeToEnd : TimeSpan.Zero,
            TimeToLiquidation: timeToLiquidation > TimeSpan.Zero ? timeToLiquidation : TimeSpan.Zero);
    }

    public string ComputeSessionId(DateTimeOffset utcNow)
    {
        var sessionTzTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow.UtcDateTime, _timeZone);
        var sessionNumber = (sessionTzTime.Hour / _settings.SessionHours) + 1;
        return ComputeSessionId(sessionTzTime, sessionNumber);
    }

    private static string ComputeSessionId(DateTime sessionTzTime, int sessionNumber)
        => $"{sessionTzTime:yyyyMMdd}-S{sessionNumber}";

    private static SessionPhase DeterminePhase(
        DateTime utcNow,
        DateTime sessionStartUtc,
        DateTime softUnwindStartUtc,
        DateTime liquidationStartUtc,
        DateTime forcedFlattenStartUtc,
        DateTime sessionEndUtc)
    {
        if (utcNow < sessionStartUtc || utcNow >= sessionEndUtc)
            return SessionPhase.SessionClosed;

        if (utcNow >= forcedFlattenStartUtc)
            return SessionPhase.ForcedFlatten;

        if (utcNow >= liquidationStartUtc)
            return SessionPhase.LiquidationOnly;

        if (utcNow >= softUnwindStartUtc)
            return SessionPhase.SoftUnwind;

        return SessionPhase.Open;
    }
}
