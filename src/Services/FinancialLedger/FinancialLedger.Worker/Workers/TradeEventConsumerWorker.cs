using FinancialLedger.Worker.Configuration;
using FinancialLedger.Worker.Domain;
using FinancialLedger.Worker.Hubs;
using FinancialLedger.Worker.Infrastructure;
using FinancialLedger.Worker.Services;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using System.Globalization;

namespace FinancialLedger.Worker.Workers;

public sealed class TradeEventConsumerWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LedgerSettings _settings;
    private readonly IHubContext<LedgerHub> _hub;
    private readonly ILogger<TradeEventConsumerWorker> _logger;

    public TradeEventConsumerWorker(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        LedgerSettings settings,
        IHubContext<LedgerHub> hub,
        ILogger<TradeEventConsumerWorker> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _settings = settings;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TradeEventConsumerWorker started");

        var db = _redis.GetDatabase();
        await EnsureConsumerGroupAsync(db);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var entries = await db.StreamReadGroupAsync(
                    _settings.RedisEventsStreamKey,
                    _settings.RedisConsumerGroup,
                    _settings.RedisConsumerName,
                    ">",
                    _settings.RedisReadBatchSize,
                    noAck: false);

                if (entries.Length == 0)
                {
                    await Task.Delay(_settings.RedisReadBlockMs, stoppingToken);
                    continue;
                }

                foreach (var entry in entries)
                {
                    var handled = await HandleEntryAsync(entry, stoppingToken);
                    if (handled)
                    {
                        await db.StreamAcknowledgeAsync(
                            _settings.RedisEventsStreamKey,
                            _settings.RedisConsumerGroup,
                            entry.Id);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while consuming ledger events stream");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task EnsureConsumerGroupAsync(IDatabase db)
    {
        try
        {
            await db.StreamCreateConsumerGroupAsync(
                _settings.RedisEventsStreamKey,
                _settings.RedisConsumerGroup,
                "$",
                createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Consumer group {Group} already exists", _settings.RedisConsumerGroup);
        }
    }

    private async Task<bool> HandleEntryAsync(StreamEntry entry, CancellationToken ct)
    {
        var evt = ParseEvent(entry);
        if (evt is null)
        {
            _logger.LogWarning("Skipping malformed ledger event with id {EntryId}", entry.Id);
            return true;
        }

        if (!LedgerEntryTypes.IsSupported(evt.Type))
        {
            _logger.LogWarning("Skipping unsupported ledger entry type {Type} for event {EntryId}", evt.Type, entry.Id);
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var accountRepo = scope.ServiceProvider.GetRequiredService<VirtualAccountRepository>();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionManagementService>();
        var ledgerRepo = scope.ServiceProvider.GetRequiredService<LedgerRepository>();
        var pnlService = scope.ServiceProvider.GetRequiredService<PnlCalculationService>();
        var sellSnapshotService = scope.ServiceProvider.GetRequiredService<EquitySellSnapshotService>();

        var accountId = evt.AccountId;
        Guid sessionId;

        if (evt.SessionId != Guid.Empty)
        {
            var routedSession = await sessionService.GetSessionByIdAsync(evt.SessionId);
            if (routedSession is not null)
            {
                sessionId = routedSession.Id;
            }
            else
            {
                _logger.LogWarning(
                    "Ledger event {TransactionId} carried unknown sessionId {SessionId}; falling back to active session routing.",
                    evt.BinanceTransactionId,
                    evt.SessionId);

                if (accountId == Guid.Empty)
                {
                    accountId = await accountRepo.GetOrCreateAccountAsync(evt.Environment);
                }

                var activeSessionFallback = await sessionService.GetActiveSessionAsync(accountId);
                sessionId = activeSessionFallback?.Id ?? await sessionService.CreateSessionAsync(
                    accountId,
                    evt.AlgorithmName,
                    _settings.DefaultInitialBalance);
            }
        }
        else
        {
            if (accountId == Guid.Empty)
            {
                accountId = await accountRepo.GetOrCreateAccountAsync(evt.Environment);
            }

            var activeSession = await sessionService.GetActiveSessionAsync(accountId);
            sessionId = activeSession?.Id ?? await sessionService.CreateSessionAsync(
                accountId,
                evt.AlgorithmName,
                _settings.DefaultInitialBalance);
        }

        var inserted = await ledgerRepo.InsertLedgerEntryAsync(
            sessionId,
            evt.BinanceTransactionId,
            evt.Type,
            evt.Amount,
            evt.Symbol,
            evt.Timestamp);

        if (!inserted)
        {
            return true;
        }

        await pnlService.InvalidateBalanceCacheAsync(sessionId);
        var balance = await pnlService.GetCurrentBalanceAsync(sessionId);

        var snapshotTriggerId = BuildSnapshotTriggerTransactionId(evt, entry.Id.ToString());

        if (string.Equals(evt.Type, LedgerEntryTypes.Commission, StringComparison.Ordinal))
        {
            var side = ParseOrderSideFromTransactionId(evt.BinanceTransactionId);
            if (side is not null)
            {
                await sellSnapshotService.CaptureSnapshotAsync(
                    sessionId,
                    snapshotTriggerId,
                    evt.Symbol,
                    evt.Timestamp,
                    side,
                    ct);
            }
        }
        else if (string.Equals(evt.Type, LedgerEntryTypes.RealizedPnl, StringComparison.Ordinal))
        {
            await sellSnapshotService.CaptureSnapshotAsync(
                sessionId,
                snapshotTriggerId,
                evt.Symbol,
                evt.Timestamp,
                EquityEventTypes.Sell,
                ct);
        }
        else if (string.Equals(evt.Type, LedgerEntryTypes.InitialFunding, StringComparison.Ordinal))
        {
            await sellSnapshotService.CaptureSnapshotAsync(
                sessionId,
                snapshotTriggerId,
                null,
                evt.Timestamp,
                EquityEventTypes.SessionStart,
                ct);
        }

        await _hub.Clients.All.SendAsync(
            "ReceiveLedgerEntry",
            new
            {
                sessionId,
                evt.BinanceTransactionId,
                evt.Type,
                evt.Amount,
                evt.Symbol,
                evt.Timestamp
            },
            ct);

        await _hub.Clients.All.SendAsync(
            "ReceiveBalanceUpdate",
            new { sessionId, balance, timestamp = DateTime.UtcNow },
            ct);

        return true;
    }

    private LedgerEvent? ParseEvent(StreamEntry entry)
    {
        var dict = entry.Values.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());

        if (!dict.TryGetValue("type", out var type) || string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        if (!dict.TryGetValue("amount", out var amountText) ||
            !decimal.TryParse(amountText, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        var accountId = dict.TryGetValue("accountId", out var accountIdText) && Guid.TryParse(accountIdText, out var parsedAccountId)
            ? parsedAccountId
            : Guid.Empty;

        var sessionId = dict.TryGetValue("sessionId", out var sessionIdText) && Guid.TryParse(sessionIdText, out var parsedSessionId)
            ? parsedSessionId
            : Guid.Empty;

        var timestamp = dict.TryGetValue("timestamp", out var timestampText) &&
            DateTime.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedTimestamp)
                ? parsedTimestamp
                : DateTime.UtcNow;

        var environment = dict.TryGetValue("environment", out var environmentText) && !string.IsNullOrWhiteSpace(environmentText)
            ? environmentText.ToUpperInvariant()
            : _settings.DefaultEnvironment;

        var algorithmName = dict.TryGetValue("algorithmName", out var algorithmNameText) && !string.IsNullOrWhiteSpace(algorithmNameText)
            ? algorithmNameText
            : _settings.DefaultAlgorithmName;

        dict.TryGetValue("transactionId", out var tranId);
        dict.TryGetValue("symbol", out var symbol);

        return new LedgerEvent(accountId, sessionId, environment, algorithmName, tranId, type.ToUpperInvariant(), amount, symbol, timestamp);
    }

    private sealed record LedgerEvent(
        Guid AccountId,
        Guid SessionId,
        string Environment,
        string AlgorithmName,
        string? BinanceTransactionId,
        string Type,
        decimal Amount,
        string? Symbol,
        DateTime Timestamp);

    private static string BuildSnapshotTriggerTransactionId(LedgerEvent evt, string fallbackEntryId)
    {
        if (string.IsNullOrWhiteSpace(evt.BinanceTransactionId))
        {
            return fallbackEntryId;
        }

        if (string.Equals(evt.Type, LedgerEntryTypes.InitialFunding, StringComparison.Ordinal))
        {
            return $"SESSION_START:{evt.SessionId}";
        }

        if (string.Equals(evt.Type, LedgerEntryTypes.Commission, StringComparison.Ordinal))
        {
            var tx = evt.BinanceTransactionId;
            var marker = ":COMMISSION:";
            var idx = tx.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                var orderId = tx[..idx];
                var sideText = tx[(idx + marker.Length)..].Trim();
                var side = string.Equals(sideText, "BUY", StringComparison.OrdinalIgnoreCase)
                    ? EquityEventTypes.Buy
                    : EquityEventTypes.Sell;
                return $"ORDER:{orderId}:{side}";
            }
        }

        if (string.Equals(evt.Type, LedgerEntryTypes.RealizedPnl, StringComparison.Ordinal))
        {
            var tx = evt.BinanceTransactionId;
            var marker = ":REALIZED_PNL";
            var idx = tx.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                var orderId = tx[..idx];
                return $"ORDER:{orderId}:{EquityEventTypes.Sell}";
            }
        }

        return evt.BinanceTransactionId;
    }

    private static string? ParseOrderSideFromTransactionId(string? transactionId)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return null;
        }

        if (transactionId.EndsWith(":BUY", StringComparison.OrdinalIgnoreCase))
        {
            return EquityEventTypes.Buy;
        }

        if (transactionId.EndsWith(":SELL", StringComparison.OrdinalIgnoreCase))
        {
            return EquityEventTypes.Sell;
        }

        return null;
    }
}
