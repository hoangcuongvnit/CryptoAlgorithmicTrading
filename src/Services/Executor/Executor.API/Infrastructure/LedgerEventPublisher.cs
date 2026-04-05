using CryptoTrading.Shared.DTOs;
using Executor.API.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Globalization;

namespace Executor.API.Infrastructure;

public sealed class LedgerEventPublisher
{
    private const string LedgerEventsStream = "ledger:events";

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
        if (!result.Success || request.Side != OrderSide.Sell)
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
        var commission = -(closedQty * (result.FilledPrice + positionBeforeFill.AvgEntryPrice) * 0.001m);
        var environment = _binanceSettings.UseTestnet ? "TESTNET" : "MAINNET";
        var algorithmName = string.IsNullOrWhiteSpace(request.StrategyName) ? "EXECUTOR" : request.StrategyName;

        await PublishEntryAsync(
            transactionId: $"{result.OrderId}:REALIZED_PNL",
            type: "REALIZED_PNL",
            amount: grossRealizedPnl,
            symbol: request.Symbol,
            environment: environment,
            algorithmName: algorithmName,
            timestamp: result.Timestamp,
            cancellationToken: cancellationToken);

        await PublishEntryAsync(
            transactionId: $"{result.OrderId}:COMMISSION",
            type: "COMMISSION",
            amount: commission,
            symbol: request.Symbol,
            environment: environment,
            algorithmName: algorithmName,
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
