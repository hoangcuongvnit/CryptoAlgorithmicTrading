using Dapper;
using FinancialLedger.Worker.Infrastructure;
using FinancialLedger.Worker.Tests.Common;
using FinancialLedger.Worker.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FinancialLedger.Worker.Tests.Integration;

[Collection("ledger-integration")]
public sealed class LedgerRepositoryIntegrationTests
{
    private readonly LedgerIntegrationFixture _fixture;

    public LedgerRepositoryIntegrationTests(LedgerIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", TestCategories.Integration)]
    public async Task InsertLedgerEntryAsync_ShouldPersistAndPreventDuplicateByTransactionId()
    {
        await _fixture.ResetDataAsync();
        var (_, sessionId) = await _fixture.SeedActiveSessionAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fixture.PostgresConnectionString,
            })
            .Build();

        var repository = new LedgerRepository(config);
        var txId = "dup-001:SELL_CASH_IN";
        var ts = DateTime.UtcNow;

        var insertedFirst = await repository.InsertLedgerEntryAsync(sessionId, txId, "SELL_CASH_IN", 50m, "BTCUSDT", ts);
        var insertedSecond = await repository.InsertLedgerEntryAsync(sessionId, txId, "SELL_CASH_IN", 999m, "BTCUSDT", ts.AddSeconds(1));

        insertedFirst.Should().BeTrue();
        insertedSecond.Should().BeFalse();

        await using var connection = new NpgsqlConnection(_fixture.PostgresConnectionString);
        var rows = (await connection.QueryAsync<(string Type, decimal Amount, string? Symbol)>(
            "SELECT type, amount, symbol FROM ledger_entries WHERE session_id = @SessionId AND binance_transaction_id = @TxId",
            new { SessionId = sessionId, TxId = txId })).ToList();

        rows.Should().HaveCount(1);
        rows[0].Type.Should().Be("SELL_CASH_IN");
        rows[0].Amount.Should().Be(50m);
        rows[0].Symbol.Should().Be("BTCUSDT");
    }
}
