namespace Gateway.API.Dashboard;

public sealed class DashboardOptions
{
    public string ConnectionStringName { get; set; } = "Postgres";

    public string[] DefaultSymbols { get; set; } =
    [
        "BTCUSDT",
        "ETHUSDT",
        "BNBUSDT",
        "SOLUSDT",
        "XRPUSDT"
    ];

    public string[] AllowedIntervals { get; set; } =
    [
        "1m",
        "5m",
        "15m",
        "1h",
        "1d"
    ];

    public int MaxCandlesPerRequest { get; set; } = 20000;

    public int MaxRangeDaysFor1m { get; set; } = 30;

    public int MaxRangeDaysForHigherIntervals { get; set; } = 365;

    public int DefaultPageSize { get; set; } = 100;

    public int MaxPageSize { get; set; } = 500;

    public int CacheSeconds { get; set; } = 20;

    public int ResolveMaxRangeDays(string interval)
    {
        return string.Equals(interval, "1m", StringComparison.OrdinalIgnoreCase)
            ? MaxRangeDaysFor1m
            : MaxRangeDaysForHigherIntervals;
    }
}