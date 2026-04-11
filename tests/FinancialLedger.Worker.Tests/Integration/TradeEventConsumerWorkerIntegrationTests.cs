using Dapper;
using FinancialLedger.Worker.Domain;
using FinancialLedger.Worker.Tests.Common;
using FinancialLedger.Worker.Tests.Fixtures;
using FinancialLedger.Worker.Workers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;

namespace FinancialLedger.Worker.Tests.Integration;

[Collection("ledger-integration")]
public sealed class TradeEventConsumerWorkerIntegrationTests
{
    private readonly LedgerIntegrationFixture _fixture;

    public TradeEventConsumerWorkerIntegrationTests(LedgerIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", TestCategories.Integration)]
    public async Task HandleEntryAsync_ShouldInsertLedgerEntry_AndKeepFieldMapping()
    {
        await _fixture.ResetDataAsync();
        var (accountId, sessionId) = await _fixture.SeedActiveSessionAsync(initialBalance: 100m);

        await using var provider = _fixture.CreateServiceProvider();
        using var scope = provider.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<TradeEventConsumerWorker>();

        var timestamp = DateTime.UtcNow;
        var entry = new StreamEntry("10-0",
        [
            new NameValueEntry("transactionId", "ord-10:BUY_CASH_OUT"),
            new NameValueEntry("type", LedgerEntryTypes.BuyCashOut),
            new NameValueEntry("amount", "-1000.50"),
            new NameValueEntry("accountId", accountId.ToString()),
            new NameValueEntry("sessionId", sessionId.ToString()),
            new NameValueEntry("environment", "TESTNET"),
            new NameValueEntry("algorithmName", "ALGO_X"),
            new NameValueEntry("symbol", "BTCUSDT"),
            new NameValueEntry("timestamp", timestamp.ToString("O")),
        ]);

        var handled = await WorkerPrivateApiInvoker.HandleEntryAsync(worker, entry, CancellationToken.None);

        handled.Should().BeTrue();

        await using var connection = new NpgsqlConnection(_fixture.PostgresConnectionString);
        var row = await connection.QuerySingleAsync<(Guid SessionId, string Type, decimal Amount, string? Symbol)>(
            """
            SELECT session_id AS SessionId, type AS Type, amount AS Amount, symbol AS Symbol
            FROM ledger_entries
            WHERE session_id = @SessionId AND binance_transaction_id = @TxId
            """,
            new { SessionId = sessionId, TxId = "ord-10:BUY_CASH_OUT" });

        row.SessionId.Should().Be(sessionId);
        row.Type.Should().Be(LedgerEntryTypes.BuyCashOut);
        row.Amount.Should().Be(-1000.50m);
        row.Symbol.Should().Be("BTCUSDT");
    }

    [Fact]
    [Trait("Category", TestCategories.Integration)]
    public async Task HandleEntryAsync_WithUnknownSession_ShouldFallbackToActiveSession()
    {
        await _fixture.ResetDataAsync();
        var (accountId, activeSessionId) = await _fixture.SeedActiveSessionAsync(initialBalance: 100m);

        await using var provider = _fixture.CreateServiceProvider();
        using var scope = provider.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<TradeEventConsumerWorker>();

        var unknownSessionId = Guid.NewGuid();
        var entry = new StreamEntry("11-0",
        [
            new NameValueEntry("transactionId", "ord-11:SELL_CASH_IN"),
            new NameValueEntry("type", LedgerEntryTypes.SellCashIn),
            new NameValueEntry("amount", "120.75"),
            new NameValueEntry("accountId", accountId.ToString()),
            new NameValueEntry("sessionId", unknownSessionId.ToString()),
            new NameValueEntry("environment", "TESTNET"),
            new NameValueEntry("algorithmName", "ALGO_X"),
            new NameValueEntry("symbol", "ETHUSDT"),
            new NameValueEntry("timestamp", DateTime.UtcNow.ToString("O")),
        ]);

        var handled = await WorkerPrivateApiInvoker.HandleEntryAsync(worker, entry, CancellationToken.None);

        handled.Should().BeTrue();

        await using var connection = new NpgsqlConnection(_fixture.PostgresConnectionString);
        var routedSession = await connection.QuerySingleAsync<Guid>(
            "SELECT session_id FROM ledger_entries WHERE binance_transaction_id = @TxId",
            new { TxId = "ord-11:SELL_CASH_IN" });

        routedSession.Should().Be(activeSessionId);
    }
}
