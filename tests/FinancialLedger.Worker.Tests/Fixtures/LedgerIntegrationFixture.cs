using Dapper;
using FinancialLedger.Worker.Configuration;
using FinancialLedger.Worker.Hubs;
using FinancialLedger.Worker.Infrastructure;
using FinancialLedger.Worker.Services;
using FinancialLedger.Worker.Tests.Common;
using FinancialLedger.Worker.Workers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace FinancialLedger.Worker.Tests.Fixtures;

public sealed class LedgerIntegrationFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("ledger_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        await InitializeSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public async Task ResetDataAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "TRUNCATE TABLE ledger_equity_snapshots, ledger_entries, test_sessions, virtual_accounts RESTART IDENTITY CASCADE;");
    }

    public async Task<(Guid AccountId, Guid SessionId)> SeedActiveSessionAsync(
        string environment = "TESTNET",
        string algorithmName = "TEST_ALGO",
        decimal initialBalance = 100m)
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(
            "INSERT INTO virtual_accounts (id, environment, base_currency) VALUES (@Id, @Environment, 'USDT');",
            new { Id = accountId, Environment = environment });

        await connection.ExecuteAsync(
            """
            INSERT INTO test_sessions (id, account_id, algorithm_name, initial_balance, start_time, status)
            VALUES (@Id, @AccountId, @AlgorithmName, @InitialBalance, @StartTime, 'ACTIVE');
            """,
            new
            {
                Id = sessionId,
                AccountId = accountId,
                AlgorithmName = algorithmName,
                InitialBalance = initialBalance,
                StartTime = now,
            });

        await connection.ExecuteAsync(
            """
            INSERT INTO ledger_entries (session_id, binance_transaction_id, type, amount, symbol, timestamp)
            VALUES (@SessionId, NULL, 'INITIAL_FUNDING', @InitialBalance, NULL, @Timestamp);
            """,
            new
            {
                SessionId = sessionId,
                InitialBalance = initialBalance,
                Timestamp = now,
            });

        return (accountId, sessionId);
    }

    public async Task<decimal> GetBalanceAsync(Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(amount), 0) FROM ledger_entries WHERE session_id = @SessionId;",
            new { SessionId = sessionId });
    }

    public ServiceProvider CreateServiceProvider(HttpMessageHandler? httpHandler = null)
    {
        var settingsMap = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = PostgresConnectionString,
            ["Redis:Connection"] = RedisConnectionString,
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settingsMap)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton(new LedgerSettings
        {
            DefaultInitialBalance = 100,
            RedisEventsStreamKey = "ledger:events",
            RedisConsumerGroup = "financial-ledger",
            RedisConsumerName = "test-consumer",
            DefaultEnvironment = "TESTNET",
            DefaultAlgorithmName = "TEST_ALGO",
            ExecutorUrl = "http://executor-test",
        });

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(RedisConnectionString));
        services.AddScoped<VirtualAccountRepository>();
        services.AddScoped<LedgerRepository>();
        services.AddScoped<EquitySnapshotRepository>();
        services.AddScoped<SessionManagementService>();
        services.AddScoped<PnlCalculationService>();
        services.AddScoped<EquitySellSnapshotService>();

        var clientProxy = new Mock<IClientProxy>();
        clientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hubClients = new Mock<IHubClients>();
        hubClients.SetupGet(x => x.All).Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<LedgerHub>>();
        hubContext.SetupGet(x => x.Clients).Returns(hubClients.Object);
        services.AddSingleton(hubContext.Object);

        services.AddLogging(x => x.AddDebug());

        services.AddHttpClient("executor", client =>
            {
                client.BaseAddress = new Uri("http://executor-test");
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
                httpHandler ?? new HttpMessageHandlerStub(System.Net.HttpStatusCode.OK, "[]"));

        services.AddScoped<TradeEventConsumerWorker>();

        return services.BuildServiceProvider();
    }

    private async Task InitializeSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(LedgerTestSchema.Sql);
    }
}

[CollectionDefinition("ledger-integration")]
public sealed class LedgerIntegrationCollection : ICollectionFixture<LedgerIntegrationFixture>
{
}
