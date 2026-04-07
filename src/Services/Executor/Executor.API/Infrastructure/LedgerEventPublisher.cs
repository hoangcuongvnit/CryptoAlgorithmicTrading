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

    public async Task PublishFromSuccessfulExecutionAsync(
        OrderRequest request,
        OrderResult result,
        PositionTracker.OpenPosition? positionBeforeFill,
        CancellationToken cancellationToken)
    {
        if (!result.Success)
        {
            return;
        }

        var environment = _binanceSettings.UseTestnet ? "TESTNET" : "MAINNET";
        var algorithmName = string.IsNullOrWhiteSpace(request.StrategyName) ? "EXECUTOR" : request.StrategyName;

        if (result.FilledQty > 0 && result.FilledPrice > 0)
        {
            var notional = result.FilledQty * result.FilledPrice;
            if (request.Side == OrderSide.Buy)
            {
                await PublishEntryAsync(
                    transactionId: $"{result.OrderId}:BUY_CASH_OUT",
                    type: "BUY_CASH_OUT",
                    amount: -notional,
                    symbol: request.Symbol,
                    environment: environment,
                    algorithmName: algorithmName,
                    sessionId: request.SessionId,
                    timestamp: result.Timestamp,
                    cancellationToken: cancellationToken);
            }
            else if (request.Side == OrderSide.Sell && positionBeforeFill is not null && positionBeforeFill.Quantity > 0)
            {
                var sellCashQty = Math.Min(result.FilledQty, positionBeforeFill.Quantity);
                if (sellCashQty > 0)
                {
                    var principalBack = sellCashQty * positionBeforeFill.AvgEntryPrice;
                    await PublishEntryAsync(
                        transactionId: $"{result.OrderId}:SELL_CASH_IN",
                        type: "SELL_CASH_IN",
                        amount: principalBack,
                        symbol: request.Symbol,
                        environment: environment,
                        algorithmName: algorithmName,
                        sessionId: request.SessionId,
                        timestamp: result.Timestamp,
                        cancellationToken: cancellationToken);
                }
            }
        }

        // Commission affects net PnL on every successful fill (BUY and SELL).
        if (result.FilledQty > 0 && result.FilledPrice > 0)
        {
            var commission = -(result.FilledQty * result.FilledPrice * CommissionRate);

            await PublishEntryAsync(
                transactionId: $"{result.OrderId}:COMMISSION:{request.Side}",
                type: "COMMISSION",
                amount: commission,
                symbol: request.Symbol,
                environment: environment,
                algorithmName: algorithmName,
                sessionId: request.SessionId,
                timestamp: result.Timestamp,
                cancellationToken: cancellationToken);
        }

        if (request.Side != OrderSide.Sell)
        {
            return;
        }

        if (positionBeforeFill is null || positionBeforeFill.Quantity <= 0)
        {
            return;
        }

        var closedQty = Math.Min(result.FilledQty, positionBeforeFill.Quantity);
        if (closedQty <= 0)
        {
            return;
        }

        var grossRealizedPnl = (result.FilledPrice - positionBeforeFill.AvgEntryPrice) * closedQty;

        await PublishEntryAsync(
            transactionId: $"{result.OrderId}:REALIZED_PNL",
            type: "REALIZED_PNL",
            amount: grossRealizedPnl,
            symbol: request.Symbol,
            environment: environment,
            algorithmName: algorithmName,
            sessionId: request.SessionId,
            timestamp: result.Timestamp,
            cancellationToken: cancellationToken);
    }

    private async Task PublishEntryAsync(
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

        try
        {
            await db.StreamAddAsync(LedgerEventsStream, entries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish ledger event {Type} for {Symbol} transaction {TransactionId}",
                type, symbol, transactionId);
        }
    }
}
