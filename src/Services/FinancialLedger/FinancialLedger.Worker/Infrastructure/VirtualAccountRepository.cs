using Dapper;
using FinancialLedger.Worker.Domain;
using Npgsql;

namespace FinancialLedger.Worker.Infrastructure;

public sealed class VirtualAccountRepository
{
    private readonly string _connectionString;

    public VirtualAccountRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");
    }

    public async Task<Guid> GetOrCreateAccountAsync(string environment, string baseCurrency = "USDT")
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        var existingId = await connection.QuerySingleOrDefaultAsync<Guid?>(
            """
            SELECT id
            FROM virtual_accounts
            WHERE environment = @Environment AND base_currency = @BaseCurrency
            """,
            new { Environment = environment, BaseCurrency = baseCurrency });

        if (existingId.HasValue)
        {
            return existingId.Value;
        }

        var newId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO virtual_accounts (id, environment, base_currency)
            VALUES (@Id, @Environment, @BaseCurrency)
            """,
            new { Id = newId, Environment = environment, BaseCurrency = baseCurrency });

        return newId;
    }

    /// <summary>
    /// Returns the account ID that has the most recently started active session,
    /// regardless of environment. Used by EquityProjectionWorker to follow whichever
    /// account is currently receiving trade events from the Executor.
    /// </summary>
    public async Task<Guid?> GetMostRecentActiveAccountAsync(string baseCurrency = "USDT")
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<Guid?>(
            """
            SELECT va.id
            FROM virtual_accounts va
            JOIN test_sessions ts ON ts.account_id = va.id
            WHERE va.base_currency = @BaseCurrency
              AND ts.status = 'ACTIVE'
            ORDER BY ts.start_time DESC
            LIMIT 1
            """,
            new { BaseCurrency = baseCurrency });
    }

    /// <summary>
    /// Returns the full account record (including environment) for the account that has
    /// the most recently started active session. Used to auto-detect the active trading
    /// environment so the UI can bootstrap the correct account without hardcoding TESTNET/MAINNET.
    /// </summary>
    public async Task<VirtualAccountDto?> GetMostRecentActiveAccountWithEnvironmentAsync(string baseCurrency = "USDT")
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<VirtualAccountDto>(
            """
            SELECT
                va.id AS Id,
                va.environment AS Environment,
                va.base_currency AS BaseCurrency,
                va.created_at AS CreatedAt
            FROM virtual_accounts va
            JOIN test_sessions ts ON ts.account_id = va.id
            WHERE va.base_currency = @BaseCurrency
              AND ts.status = 'ACTIVE'
            ORDER BY ts.start_time DESC
            LIMIT 1
            """,
            new { BaseCurrency = baseCurrency });
    }

    public async Task<VirtualAccountDto?> GetAccountAsync(Guid accountId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<VirtualAccountDto>(
            """
            SELECT
                id AS Id,
                environment AS Environment,
                base_currency AS BaseCurrency,
                created_at AS CreatedAt
            FROM virtual_accounts
            WHERE id = @AccountId
            """,
            new { AccountId = accountId });
    }
}
