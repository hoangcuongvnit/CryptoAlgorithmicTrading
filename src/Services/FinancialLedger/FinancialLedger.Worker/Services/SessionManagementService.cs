using Dapper;
using FinancialLedger.Worker.Domain;
using FinancialLedger.Worker.Infrastructure;
using Npgsql;

namespace FinancialLedger.Worker.Services;

public sealed class SessionManagementService
{
    private readonly string _connectionString;
    private readonly LedgerRepository _ledgerRepository;
    private readonly ILogger<SessionManagementService> _logger;

    public SessionManagementService(
        IConfiguration configuration,
        LedgerRepository ledgerRepository,
        ILogger<SessionManagementService> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");
        _ledgerRepository = ledgerRepository;
        _logger = logger;
    }

    public async Task<Guid> CreateSessionAsync(Guid accountId, string algorithmName, decimal initialBalance)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var tx = await connection.BeginTransactionAsync();

        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await connection.ExecuteAsync(
            """
            INSERT INTO test_sessions (id, account_id, algorithm_name, initial_balance, start_time, status)
            VALUES (@Id, @AccountId, @AlgorithmName, @InitialBalance, @StartTime, 'ACTIVE')
            """,
            new
            {
                Id = sessionId,
                AccountId = accountId,
                AlgorithmName = algorithmName,
                InitialBalance = initialBalance,
                StartTime = now,
            },
            tx);

        await _ledgerRepository.InsertLedgerEntryAsync(
            sessionId,
            null,
            LedgerEntryTypes.InitialFunding,
            initialBalance,
            null,
            now,
            tx);

        await tx.CommitAsync();

        _logger.LogInformation("Created session {SessionId} for account {AccountId}", sessionId, accountId);
        return sessionId;
    }

    public async Task<Guid> ResetSessionAsync(Guid accountId, decimal newInitialBalance, string algorithmName)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var tx = await connection.BeginTransactionAsync();
        var now = DateTime.UtcNow;

        await connection.ExecuteAsync(
            """
            UPDATE test_sessions
            SET status = 'ARCHIVED', end_time = @Now, updated_at = @Now
            WHERE account_id = @AccountId AND status = 'ACTIVE'
            """,
            new { AccountId = accountId, Now = now },
            tx);

        var newSessionId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO test_sessions (id, account_id, algorithm_name, initial_balance, start_time, status)
            VALUES (@Id, @AccountId, @AlgorithmName, @InitialBalance, @StartTime, 'ACTIVE')
            """,
            new
            {
                Id = newSessionId,
                AccountId = accountId,
                AlgorithmName = algorithmName,
                InitialBalance = newInitialBalance,
                StartTime = now,
            },
            tx);

        await _ledgerRepository.InsertLedgerEntryAsync(
            newSessionId,
            null,
            LedgerEntryTypes.InitialFunding,
            newInitialBalance,
            null,
            now,
            tx);

        await tx.CommitAsync();

        _logger.LogInformation("Reset session for account {AccountId}. New session {SessionId}", accountId, newSessionId);
        return newSessionId;
    }

    public async Task<SessionDto?> GetActiveSessionAsync(Guid accountId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<SessionDto>(
            """
            SELECT
                id AS Id,
                account_id AS AccountId,
                algorithm_name AS AlgorithmName,
                initial_balance AS InitialBalance,
                start_time AS StartTime,
                end_time AS EndTime,
                status AS Status
            FROM test_sessions
            WHERE account_id = @AccountId AND status = 'ACTIVE'
            ORDER BY start_time DESC
            LIMIT 1
            """,
            new { AccountId = accountId });
    }

    public async Task<SessionDto?> GetSessionByIdAsync(Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<SessionDto>(
            """
            SELECT
                id AS Id,
                account_id AS AccountId,
                algorithm_name AS AlgorithmName,
                initial_balance AS InitialBalance,
                start_time AS StartTime,
                end_time AS EndTime,
                status AS Status
            FROM test_sessions
            WHERE id = @SessionId
            LIMIT 1
            """,
            new { SessionId = sessionId });
    }

    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(Guid accountId, string? status)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        var whereClause = "WHERE account_id = @AccountId";
        var parameters = new DynamicParameters();
        parameters.Add("AccountId", accountId);

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            whereClause += " AND status = @Status";
            parameters.Add("Status", status.ToUpperInvariant());
        }

        var sessions = (await connection.QueryAsync<SessionDto>(
            $"""
            SELECT
                id AS Id,
                account_id AS AccountId,
                algorithm_name AS AlgorithmName,
                initial_balance AS InitialBalance,
                start_time AS StartTime,
                end_time AS EndTime,
                status AS Status
            FROM test_sessions
            {whereClause}
            ORDER BY start_time DESC
            """,
            parameters)).ToList();

        return sessions;
    }
}
