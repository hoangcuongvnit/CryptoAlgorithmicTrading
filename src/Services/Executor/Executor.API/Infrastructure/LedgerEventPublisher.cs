using CryptoTrading.Shared.DTOs;
using Executor.API.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Globalization;

namespace Executor.API.Infrastructure;

public sealed class LedgerEventPublisher
{
    private const string LedgerEventsStream = "ledger:events";
    private const decimal CommissionRate = 0.001m;
    private const int MaxPublishAttempts = 3;
    private const int InitialRetryDelayMs = 200;

    private readonly IConnectionMultiplexer _redis;
    private readonly BinanceSettings _binanceSettings;
    private readonly ILogger<LedgerEventPublisher> _logger;

    public LedgerEventPublisher(
        IConnectionMultiplexer redis,
        IOptions<BinanceSettings> binanceSettings,
        ILogger<LedgerEventPublisher> logger)
    {
        _redis = redis;
        _binanceSettings = binanceSettings.Value;
        _logger = logger;
    }

    public async Task<bool> PublishFromSuccessfulExecutionAsync(
        OrderRequest request,
        OrderResult result,
        PositionTracker.OpenPosition? positionBeforeFill,
        CancellationToken cancellationToken)
    {
        if (!result.Success)
        {
            return true;
        }

        var environment = _binanceSettings.UseTestnet ? "TESTNET" : "MAINNET";
        var algorithmName = string.IsNullOrWhiteSpace(request.StrategyName) ? "EXECUTOR" : request.StrategyName;
        var allSucceeded = true;

        if (result.FilledQty > 0 && result.FilledPrice > 0)
        {
            var notional = result.FilledQty * result.FilledPrice;
            if (request.Side == OrderSide.Buy)
            {
                if (await PublishEntryAsync(
                    transactionId: $"{result.OrderId}:BUY_CASH_OUT",
                    type: "BUY_CASH_OUT",
                    amount: -notional,
                    symbol: request.Symbol,
                    environment: environment,
                    algorithmName: algorithmName,
                    sessionId: request.SessionId,
                    timestamp: result.Timestamp,
                    cancellationToken: cancellationToken) is false)
                {
                    allSucceeded = false;
                }
            }
            else if (request.Side == OrderSide.Sell)
            {
                // Determine how many units to return cash for.
                // When the position is tracked we cap at the tracked quantity (partial-sell safety).
                // When it is untracked (e.g. IsReduceOnly liquidation after restart) we fall back
                // to the full filled quantity so balance is always credited.
                var sellCashQty = positionBeforeFill is not null && positionBeforeFill.Quantity > 0
                    ? Math.Min(result.FilledQty, positionBeforeFill.Quantity)
                    : result.FilledQty;

                if (sellCashQty > 0)
                {
                    // When entry price is known: return only the cost basis here; the profit/loss
                    // difference is published separately as REALIZED_PNL below so the two entries
                    // together equal the full fill proceeds.
                    // When entry price is unknown (untracked position): return full fill proceeds
                    // directly so the balance is always correct even without REALIZED_PNL.
                    var trackedEntryPrice = positionBeforeFill?.AvgEntryPrice ?? 0m;
                    var cashInPrice = trackedEntryPrice > 0 ? trackedEntryPrice : result.FilledPrice;

                    var principalBack = sellCashQty * cashInPrice;
                    if (await PublishEntryAsync(
                        transactionId: $"{result.OrderId}:SELL_CASH_IN",
                        type: "SELL_CASH_IN",
                        amount: principalBack,
                        symbol: request.Symbol,
                        environment: environment,
                        algorithmName: algorithmName,
                        sessionId: request.SessionId,
                        timestamp: result.Timestamp,
                        cancellationToken: cancellationToken) is false)
                    {
                        allSucceeded = false;
                    }
                }
            }
        }

        // Commission affects net PnL on every successful fill (BUY and SELL).
        if (result.FilledQty > 0 && result.FilledPrice > 0)
        {
            var commission = -(result.FilledQty * result.FilledPrice * CommissionRate);

            if (await PublishEntryAsync(
                transactionId: $"{result.OrderId}:COMMISSION:{request.Side}",
                type: "COMMISSION",
                amount: commission,
                symbol: request.Symbol,
                environment: environment,
                algorithmName: algorithmName,
                sessionId: request.SessionId,
                timestamp: result.Timestamp,
                cancellationToken: cancellationToken) is false)
            {
                allSucceeded = false;
            }
        }

        if (request.Side != OrderSide.Sell)
        {
            return allSucceeded;
        }

        // REALIZED_PNL: only published when entry price is known (tracked position).
        // If position was untracked, full proceeds were already included in SELL_CASH_IN above,
        // so skipping REALIZED_PNL here avoids double-counting.
        if (positionBeforeFill is null || positionBeforeFill.Quantity <= 0 || positionBeforeFill.AvgEntryPrice <= 0)
        {
            return allSucceeded;
        }

        var closedQty = Math.Min(result.FilledQty, positionBeforeFill.Quantity);
        if (closedQty <= 0)
        {
            return allSucceeded;
        }

        var grossRealizedPnl = (result.FilledPrice - positionBeforeFill.AvgEntryPrice) * closedQty;

        if (await PublishEntryAsync(
            transactionId: $"{result.OrderId}:REALIZED_PNL",
            type: "REALIZED_PNL",
            amount: grossRealizedPnl,
            symbol: request.Symbol,
            environment: environment,
            algorithmName: algorithmName,
            sessionId: request.SessionId,
            timestamp: result.Timestamp,
            cancellationToken: cancellationToken) is false)
        {
            allSucceeded = false;
        }

        return allSucceeded;
    }

    private async Task<bool> PublishEntryAsync(
        string transactionId,
        string type,
        decimal amount,
        string symbol,
        string environment,
        string algorithmName,
        string? sessionId,
        DateTime timestamp,
        CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();

        var entries = new NameValueEntry[]
        {
            new("transactionId", transactionId),
            new("type", type),
            new("amount", amount.ToString("0.########", CultureInfo.InvariantCulture)),
            new("accountId", string.Empty),
            new("sessionId", sessionId ?? string.Empty),
            new("environment", environment),
            new("algorithmName", algorithmName),
            new("timestamp", timestamp.ToString("O")),
            new("symbol", symbol)
        };

        for (var attempt = 1; attempt <= MaxPublishAttempts; attempt++)
        {
            try
            {
                await db.StreamAddAsync(LedgerEventsStream, entries);
                return true;
            }
            catch (Exception ex) when (attempt < MaxPublishAttempts)
            {
                var delayMs = InitialRetryDelayMs * (1 << (attempt - 1));
                _logger.LogWarning(ex,
                    "Ledger event publish failed (attempt {Attempt}/{MaxAttempts}) for {Type} {Symbol} {TransactionId}. Retrying in {DelayMs}ms.",
                    attempt,
                    MaxPublishAttempts,
                    type,
                    symbol,
                    transactionId,
                    delayMs);
                await Task.Delay(delayMs, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to publish ledger event after {MaxAttempts} attempts: {Type} {Symbol} {TransactionId}",
                    MaxPublishAttempts,
                    type,
                    symbol,
                    transactionId);
                return false;
            }
        }

        return false;
    }
}
