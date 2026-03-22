using CryptoTrading.Shared.Constants;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Json;
using Executor.API.Configuration;
using Executor.API.Services;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Executor.API.Infrastructure;

/// <summary>
/// Phase 2.2: Background service that implements a 2-stage partial take-profit strategy.
/// <para>
/// Stage 1 (default +1.5%): Close 50% of the position and note the breakeven price.
/// Stage 2 (default +3.0%): Close the remaining 50%.
/// </para>
/// The monitor is self-contained in the Executor — it subscribes to <c>price:*</c> ticks
/// and uses <see cref="PositionTracker"/> to find open BUY positions.
/// Only active when <see cref="PartialTpSettings.Enabled"/> is true.
/// </summary>
public sealed class PartialTpMonitorService : BackgroundService
{
    /// <summary>Per-symbol partial TP tracking state.</summary>
    private sealed class PartialTpState
    {
        public decimal EntryPrice { get; init; }
        public int Stage1Triggered;  // 0 = not triggered, 1 = triggered (atomic via Interlocked)
        public int Stage2Triggered;
    }

    private readonly PositionTracker _positionTracker;
    private readonly OrderExecutionService _executionService;
    private readonly PartialTpSettings _settings;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PartialTpMonitorService> _logger;

    // Keyed by symbol; reset when entry price changes by more than 0.1%
    private readonly ConcurrentDictionary<string, PartialTpState> _states = new(StringComparer.OrdinalIgnoreCase);

    public PartialTpMonitorService(
        PositionTracker positionTracker,
        OrderExecutionService executionService,
        IOptions<TradingSettings> settings,
        IConnectionMultiplexer redis,
        ILogger<PartialTpMonitorService> logger)
    {
        _positionTracker = positionTracker;
        _executionService = executionService;
        _settings = settings.Value.PartialTp;
        _redis = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Partial TP monitor disabled via config");
            return;
        }

        var subscriber = _redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            new RedisChannel(RedisChannels.PricePattern, RedisChannel.PatternMode.Pattern),
            async (_, msg) => await OnPriceTickAsync(msg, stoppingToken));

        _logger.LogInformation("Partial TP monitor started (TP1={Tp1}% × 50%, TP2={Tp2}%)",
            _settings.Stage1ProfitPercent, _settings.Stage2ProfitPercent);

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (TaskCanceledException) { }

        await subscriber.UnsubscribeAsync(
            new RedisChannel(RedisChannels.PricePattern, RedisChannel.PatternMode.Pattern));
    }

    private async Task OnPriceTickAsync(RedisValue payload, CancellationToken ct)
    {
        if (payload.IsNullOrEmpty) return;

        PriceTick? tick;
        try
        {
            tick = JsonSerializer.Deserialize((string)payload!, TradingJsonContext.Default.PriceTick);
        }
        catch { return; }

        if (tick is null || tick.Price <= 0) return;

        var positions = _positionTracker.GetRawPositions();
        var position = positions.FirstOrDefault(p =>
            string.Equals(p.Symbol, tick.Symbol, StringComparison.OrdinalIgnoreCase));

        if (position is null || position.Quantity <= 0)
        {
            _states.TryRemove(tick.Symbol, out _);
            return;
        }

        // Get or initialise state; reset if entry price drifted (e.g. position was topped up)
        var state = _states.GetOrAdd(tick.Symbol, _ => new PartialTpState { EntryPrice = position.AvgEntryPrice });
        if (Math.Abs(state.EntryPrice - position.AvgEntryPrice) / position.AvgEntryPrice > 0.001m)
        {
            state = new PartialTpState { EntryPrice = position.AvgEntryPrice };
            _states[tick.Symbol] = state;
        }

        var currentPrice = tick.Price;
        var entry = state.EntryPrice;
        if (entry <= 0) return;

        var tp1Price = entry * (1m + _settings.Stage1ProfitPercent / 100m);
        var tp2Price = entry * (1m + _settings.Stage2ProfitPercent / 100m);

        // Stage 1: close Stage1CloseRatio of position (atomic check-and-set to prevent duplicate closes)
        if (state.Stage1Triggered == 0 && currentPrice >= tp1Price
            && Interlocked.CompareExchange(ref state.Stage1Triggered, 1, 0) == 0)
        {
            var closeQty = Math.Round(position.Quantity * _settings.Stage1CloseRatio, 8);
            if (closeQty > 0)
            {
                _logger.LogInformation(
                    "Partial TP Stage 1 triggered for {Symbol}: price={Price:F2} >= tp1={Tp1:F2}, closing {Qty}",
                    tick.Symbol, currentPrice, tp1Price, closeQty);

                await PlacePartialCloseAsync(tick.Symbol, closeQty, currentPrice, "PartialTP_Stage1", ct);
            }
        }

        // Stage 2: close remaining position (atomic check-and-set)
        if (state.Stage1Triggered == 1 && state.Stage2Triggered == 0 && currentPrice >= tp2Price
            && Interlocked.CompareExchange(ref state.Stage2Triggered, 1, 0) == 0)
        {
            // Refresh position quantity after Stage 1 fill
            positions = _positionTracker.GetRawPositions();
            position = positions.FirstOrDefault(p =>
                string.Equals(p.Symbol, tick.Symbol, StringComparison.OrdinalIgnoreCase));

            if (position is not null && position.Quantity > 0)
            {
                _logger.LogInformation(
                    "Partial TP Stage 2 triggered for {Symbol}: price={Price:F2} >= tp2={Tp2:F2}, closing {Qty}",
                    tick.Symbol, currentPrice, tp2Price, position.Quantity);

                await PlacePartialCloseAsync(tick.Symbol, position.Quantity, currentPrice, "PartialTP_Stage2", ct);
            }
        }
    }

    private async Task PlacePartialCloseAsync(
        string symbol, decimal quantity, decimal price, string strategyTag, CancellationToken ct)
    {
        try
        {
            var request = new OrderRequest
            {
                Symbol = symbol,
                Side = OrderSide.Sell,
                Type = OrderType.Market,
                Quantity = quantity,
                Price = price,
                StrategyName = strategyTag,
                IsReduceOnly = true
            };

            await _executionService.ExecuteOrderAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Partial TP close failed for {Symbol}", symbol);
        }
    }
}
