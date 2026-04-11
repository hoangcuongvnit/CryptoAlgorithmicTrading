using FinancialLedger.Worker.Configuration;
using FinancialLedger.Worker.Hubs;
using FinancialLedger.Worker.Services;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using System.Globalization;
using System.Text.Json.Serialization;

namespace FinancialLedger.Worker.Workers;

/// <summary>
/// Periodically fetches open positions from Executor, reads current mark prices from Redis,
/// computes unrealized PnL, and broadcasts RealTimeEquity via SignalR.
/// RealTimeEquity formula is session-aware:
/// - cash-flow mode: CurrentBalance + HoldingsMarketValue
/// - legacy mode:    CurrentBalance + UnrealizedPnL
/// </summary>
public sealed class EquityProjectionWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<LedgerHub> _hub;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LedgerSettings _settings;
    private readonly ILogger<EquityProjectionWorker> _logger;

    public EquityProjectionWorker(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        IHubContext<LedgerHub> hub,
        IHttpClientFactory httpClientFactory,
        LedgerSettings settings,
        ILogger<EquityProjectionWorker> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _hub = hub;
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EquityProjectionWorker started (interval: {Interval}ms)", _settings.EquityProjectionIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProjectEquityAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during equity projection");
            }

            await Task.Delay(_settings.EquityProjectionIntervalMs, stoppingToken);
        }
    }

    private async Task ProjectEquityAsync(CancellationToken ct)
    {
        var positions = await FetchOpenPositionsAsync(ct);

        var unrealizedPnl = 0m;
        var holdingsMarketValue = 0m;
        var positionSnapshots = new List<object>();

        foreach (var pos in positions)
        {
            var markPrice = await GetMarkPriceFromRedisAsync(pos.Symbol);
            if (markPrice <= 0m)
            {
                // Fallback to currentPrice from Executor if Redis price unavailable
                markPrice = pos.CurrentPrice;
            }

            var posUnrealized = pos.Quantity * (markPrice - pos.EntryPrice);
            unrealizedPnl += posUnrealized;
            holdingsMarketValue += pos.Quantity * markPrice;

            positionSnapshots.Add(new
            {
                symbol = pos.Symbol,
                quantity = pos.Quantity,
                entryPrice = pos.EntryPrice,
                markPrice,
                unrealizedPnl = decimal.Round(posUnrealized, 4),
            });
        }

        // Get current realized balance from active session
        using var scope = _scopeFactory.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionManagementService>();
        var pnlService = scope.ServiceProvider.GetRequiredService<PnlCalculationService>();
        var accountRepo = scope.ServiceProvider.GetRequiredService<Infrastructure.VirtualAccountRepository>();
        var ledgerRepo = scope.ServiceProvider.GetRequiredService<Infrastructure.LedgerRepository>();

        // Use the account that is most recently active (regardless of environment),
        // so that equity projection follows whichever account is receiving ledger events
        // from the Executor (MAINNET or TESTNET mode).
        var accountId = await accountRepo.GetMostRecentActiveAccountAsync()
            ?? await accountRepo.GetOrCreateAccountAsync(_settings.DefaultEnvironment);
        var session = await sessionService.GetActiveSessionAsync(accountId);
        if (session is null)
        {
            return;
        }

        var currentBalance = await pnlService.GetCurrentBalanceAsync(session.Id);
        var usesCashFlowMode = await ledgerRepo.HasCashFlowEntriesAsync(session.Id);
        var realTimeEquity = usesCashFlowMode
            ? currentBalance + holdingsMarketValue
            : currentBalance + unrealizedPnl;

        await _hub.Clients.All.SendAsync(
            "ReceiveEquityUpdate",
            new
            {
                sessionId = session.Id,
                usesCashFlowMode,
                currentBalance,
                unrealizedPnl = decimal.Round(unrealizedPnl, 4),
                holdingsMarketValue = decimal.Round(holdingsMarketValue, 4),
                realTimeEquity = decimal.Round(realTimeEquity, 4),
                positions = positionSnapshots,
                timestamp = DateTime.UtcNow,
            },
            ct);
    }

    private async Task<IReadOnlyList<PositionSnapshot>> FetchOpenPositionsAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.ExecutorUrl))
        {
            return [];
        }

        try
        {
            var client = _httpClientFactory.CreateClient("executor");
            var response = await client.GetAsync("/api/trading/positions", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Executor positions endpoint returned {Status}", response.StatusCode);
                return [];
            }

            var positions = await response.Content.ReadFromJsonAsync<List<ExecutorPositionDto>>(cancellationToken: ct);
            if (positions is null)
            {
                return [];
            }

            return positions.Select(p => new PositionSnapshot(p.Symbol, p.Quantity, p.EntryPrice, p.CurrentPrice)).ToList();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not reach Executor service for positions");
            return [];
        }
    }

    private async Task<decimal> GetMarkPriceFromRedisAsync(string symbol)
    {
        try
        {
            var db = _redis.GetDatabase();
            // price:{SYMBOL} is a Redis pub/sub channel; the latest close price is stored as a STRING
            // by convention: "ledger:price:{SYMBOL}" or we parse from recent messages
            // Fallback: try reading from "price:latest:{SYMBOL}" key if Ingestor publishes it
            var key = $"price:latest:{symbol.ToUpperInvariant()}";
            var value = await db.StringGetAsync(key);

            if (value.HasValue && decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
            {
                return price;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read mark price for {Symbol} from Redis", symbol);
        }

        return 0m;
    }

    private sealed record PositionSnapshot(string Symbol, decimal Quantity, decimal EntryPrice, decimal CurrentPrice);

    private sealed record ExecutorPositionDto(
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("quantity")] decimal Quantity,
        [property: JsonPropertyName("entryPrice")] decimal EntryPrice,
        [property: JsonPropertyName("currentPrice")] decimal CurrentPrice);
}
