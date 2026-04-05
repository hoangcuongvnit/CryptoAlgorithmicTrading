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
