using Dapper;
using FinancialLedger.Worker.Domain;
using FinancialLedger.Worker.Tests.Common;
using FinancialLedger.Worker.Tests.Fixtures;
using FinancialLedger.Worker.Workers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;

namespace FinancialLedger.Worker.Tests.Smoke;

[Collection("ledger-integration")]
public sealed class FinancialLedgerSyncSmokeTests
{
    private readonly LedgerIntegrationFixture _fixture;

    public FinancialLedgerSyncSmokeTests(LedgerIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", TestCategories.Smoke)]
    public async Task SyncPipeline_ShouldMatchExpectedBalance_AfterBuyCommissionSellRealizedPnl()
    {
        await _fixture.ResetDataAsync();
        var (accountId, sessionId) = await _fixture.SeedActiveSessionAsync(initialBalance: 100m);

        await using var provider = _fixture.CreateServiceProvider();
        using var scope = provider.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<TradeEventConsumerWorker>();

        var now = DateTime.UtcNow;

        var events = new[]
        {
            CreateEvent("100-0", "ord-100:BUY_CASH_OUT", LedgerEntryTypes.BuyCashOut, -50m, accountId, sessionId, "BTCUSDT", now),
            CreateEvent("101-0", "ord-100:COMMISSION:BUY", LedgerEntryTypes.Commission, -0.05m, accountId, sessionId, "BTCUSDT", now.AddSeconds(1)),
            CreateEvent("102-0", "ord-101:SELL_CASH_IN", LedgerEntryTypes.SellCashIn, 60m, accountId, sessionId, "BTCUSDT", now.AddSeconds(2)),
            CreateEvent("103-0", "ord-101:REALIZED_PNL", LedgerEntryTypes.RealizedPnl, 10m, accountId, sessionId, "BTCUSDT", now.AddSeconds(3)),
        };

        foreach (var evt in events)
        {
            var handled = await WorkerPrivateApiInvoker.HandleEntryAsync(worker, evt, CancellationToken.None);
            handled.Should().BeTrue();
        }

        var expectedBalance = ExpectedLedgerCalculator.Calculate(100m, [-50m, -0.05m, 60m, 10m]);
        var actualBalance = await _fixture.GetBalanceAsync(sessionId);

        actualBalance.Should().Be(expectedBalance);

        await using var connection = new NpgsqlConnection(_fixture.PostgresConnectionString);
        var dbRows = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM ledger_entries WHERE session_id = @SessionId AND binance_transaction_id IS NOT NULL",
            new { SessionId = sessionId });

        dbRows.Should().Be(4);
    }

    private static StreamEntry CreateEvent(
        string entryId,
        string transactionId,
        string type,
        decimal amount,
        Guid accountId,
        Guid sessionId,
        string symbol,
        DateTime timestamp)
    {
        return new StreamEntry(entryId,
        [
            new NameValueEntry("transactionId", transactionId),
            new NameValueEntry("type", type),
            new NameValueEntry("amount", amount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new NameValueEntry("accountId", accountId.ToString()),
            new NameValueEntry("sessionId", sessionId.ToString()),
            new NameValueEntry("environment", "TESTNET"),
            new NameValueEntry("algorithmName", "SMOKE_ALGO"),
            new NameValueEntry("symbol", symbol),
            new NameValueEntry("timestamp", timestamp.ToString("O")),
        ]);
    }

    private static class ExpectedLedgerCalculator
    {
        public static decimal Calculate(decimal initialFunding, IReadOnlyList<decimal> deltas)
        {
            var total = initialFunding;
            foreach (var delta in deltas)
            {
                total += delta;
            }

            return total;
        }
    }
}
