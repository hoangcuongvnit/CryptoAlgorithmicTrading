using FinancialLedger.Worker.Domain;
using FinancialLedger.Worker.Infrastructure;
using StackExchange.Redis;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FinancialLedger.Worker.Services;

public sealed class EquitySellSnapshotService
{
    private readonly EquitySnapshotRepository _snapshotRepository;
    private readonly PnlCalculationService _pnlService;
    private readonly LedgerRepository _ledgerRepository;
    private readonly IConnectionMultiplexer _redis;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EquitySellSnapshotService> _logger;

    public EquitySellSnapshotService(
        EquitySnapshotRepository snapshotRepository,
        PnlCalculationService pnlService,
        LedgerRepository ledgerRepository,
        IConnectionMultiplexer redis,
        IHttpClientFactory httpClientFactory,
        ILogger<EquitySellSnapshotService> logger)
    {
        _snapshotRepository = snapshotRepository;
        _pnlService = pnlService;
        _ledgerRepository = ledgerRepository;
        _redis = redis;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task CaptureSnapshotAsync(
        Guid sessionId,
        string triggerTransactionId,
        string? triggerSymbol,
        DateTime snapshotTime,
        string eventType,
        CancellationToken cancellationToken)
    {
        var currentBalance = await _pnlService.GetCurrentBalanceAsync(sessionId);
        var positions = await FetchOpenPositionsAsync(cancellationToken);

        var holdings = new List<EquityHoldingSnapshot>();
        var holdingsMarketValue = 0m;
        var unrealizedPnl = 0m;

        foreach (var position in positions)
        {
            if (position.Quantity <= 0)
            {
                continue;
            }

            var markPrice = await GetMarkPriceAsync(position.Symbol);
            if (markPrice <= 0m)
            {
                markPrice = position.CurrentPrice;
            }

            if (markPrice <= 0m)
            {
                _logger.LogWarning(
                    "Skip equity holding valuation for {Symbol}: mark/current price unavailable",
                    position.Symbol);
                continue;
            }

            var marketValue = decimal.Round(position.Quantity * markPrice, 8);
            holdingsMarketValue += marketValue;
            unrealizedPnl += decimal.Round(position.Quantity * (markPrice - position.EntryPrice), 8);

            holdings.Add(new EquityHoldingSnapshot(
                position.Symbol,
                decimal.Round(position.Quantity, 8),
                decimal.Round(markPrice, 8),
                marketValue));
        }

        var usesCashFlowMode = await _ledgerRepository.HasCashFlowEntriesAsync(sessionId);
        var totalEquity = usesCashFlowMode
            ? decimal.Round(currentBalance + holdingsMarketValue, 8)
            : decimal.Round(currentBalance + unrealizedPnl, 8);
        var snapshot = new SellEquitySnapshot(
            sessionId,
            triggerTransactionId,
            triggerSymbol,
            snapshotTime,
            decimal.Round(currentBalance, 8),
            decimal.Round(holdingsMarketValue, 8),
            totalEquity,
            holdings,
            eventType);

        var inserted = await _snapshotRepository.InsertSellSnapshotAsync(snapshot);
        if (!inserted)
        {
            _logger.LogDebug(
                "Skipped duplicate equity snapshot for session {SessionId}, tx {TransactionId}",
                sessionId,
                triggerTransactionId);
        }
    }

    private async Task<IReadOnlyList<PositionDto>> FetchOpenPositionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("executor");
            using var response = await client.GetAsync("/api/trading/positions", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Could not fetch open positions for equity snapshot. Status={StatusCode}",
                    (int)response.StatusCode);
                return [];
            }

            var positions = await response.Content.ReadFromJsonAsync<List<PositionDto>>(cancellationToken: cancellationToken);
            return positions ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not fetch open positions for equity snapshot");
            return [];
        }
    }

    private async Task<decimal> GetMarkPriceAsync(string symbol)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"price:latest:{symbol.ToUpperInvariant()}";
            var value = await db.StringGetAsync(key);

            if (value.HasValue && decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read mark price for {Symbol}", symbol);
        }

        return 0m;
    }

    private sealed record PositionDto(
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("quantity")] decimal Quantity,
        [property: JsonPropertyName("entryPrice")] decimal EntryPrice,
        [property: JsonPropertyName("currentPrice")] decimal CurrentPrice);
}
