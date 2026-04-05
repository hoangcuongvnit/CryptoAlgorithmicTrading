using FinancialLedger.Worker.Domain;
using FinancialLedger.Worker.Infrastructure;
using StackExchange.Redis;
using System.Globalization;

namespace FinancialLedger.Worker.Services;

public sealed class PnlCalculationService
{
    private const string CachePrefix = "ledger:pnl:balance:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly LedgerRepository _ledgerRepository;
    private readonly IConnectionMultiplexer _redis;

    public PnlCalculationService(LedgerRepository ledgerRepository, IConnectionMultiplexer redis)
    {
        _ledgerRepository = ledgerRepository;
        _redis = redis;
    }

    public async Task<decimal> GetCurrentBalanceAsync(Guid sessionId)
    {
        var db = _redis.GetDatabase();
        var cacheKey = $"{CachePrefix}{sessionId}";

        var cachedValue = await db.StringGetAsync(cacheKey);
        if (cachedValue.HasValue && decimal.TryParse(cachedValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var cachedBalance))
        {
            return cachedBalance;
        }

        var balance = await _ledgerRepository.GetCurrentBalanceAsync(sessionId);
        await db.StringSetAsync(cacheKey, balance.ToString(CultureInfo.InvariantCulture), CacheTtl);
        return balance;
    }

    public async Task<decimal> CalculateNetPnlAsync(Guid sessionId, string? symbol = null)
    {
        var pnlBySymbol = await _ledgerRepository.GetPnlBySymbolAsync(sessionId);

        if (string.IsNullOrWhiteSpace(symbol))
        {
            return pnlBySymbol.Values.Sum(value => value.NetPnl);
        }

        return pnlBySymbol.TryGetValue(symbol, out var breakdown) ? breakdown.NetPnl : 0m;
    }

    public async Task<IReadOnlyDictionary<string, PnlBreakdown>> GetPnlBreakdownAsync(Guid sessionId)
    {
        return await _ledgerRepository.GetPnlBySymbolAsync(sessionId);
    }

    public static decimal CalculateRoePercent(decimal netPnl, decimal initialBalance)
    {
        if (initialBalance == 0m)
        {
            return 0m;
        }

        return (netPnl / initialBalance) * 100m;
    }

    public async Task InvalidateBalanceCacheAsync(Guid sessionId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"{CachePrefix}{sessionId}");
    }
}
