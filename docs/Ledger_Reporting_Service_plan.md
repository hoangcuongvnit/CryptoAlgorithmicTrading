# Plan: FinancialLedger Service — Backend + Frontend

**Status**: Final (Ready for Implementation)  
**Date**: April 5, 2026  
**Author**: AI Planning Agent  
**Scope**: New microservice with immutable ledger, P&L reporting, and real-time dashboard

---

## Executive Summary

Build a new **FinancialLedger** microservice implementing:
- **Immutable ledger** pattern (INSERT-only, no UPDATEs) with durable event ingestion
- **P&L calculations** (net PnL, ROE, balance formulas per the specification)
- **Saga-based session reset** (halt/close/confirm before session rollover)
- **Real-time dashboard** (React app with SignalR for live realized + unrealized PnL)
- **Dual live modes** (support both Mainnet and Testnet; no paper trading mode)

### Key Numbers
- **Backend service**: HTTP on port 5097 (hybrid REST+Worker pattern)
- **Frontend app**: React on port 5098 (separate from existing `frontend/`)
- **Database**: PostgreSQL (3 new tables: `virtual_accounts`, `test_sessions`, `ledger_entries`)
- **Integrations**: Executor User Data Stream, reliable broker (Redis Streams or RabbitMQ/MassTransit), Trading Engine reset orchestration
- **Real-time**: SignalR hub for live realized/unrealized P&L and equity broadcasts

---

## Requirements Summary

### Functional Requirements
1. **Ledger Management**
   - Record cash flows with exact precision (DECIMAL data types)
   - Track: initial funding, realized P&L, commissions, funding fees, withdrawals
   - Idempotent inserts (prevent duplicates via Binance `tranId` unique constraint)
   - Immutable history (INSERT-only, audit trail)

2. **P&L Reporting**
   - Current balance: ∑ all ledger entries for session
   - Net PnL: realized_pnl + commission + funding_fee
   - ROE: (Net PnL / initial margin) × 100%
   - Breakdown by symbol

3. **Session Management**
   - Create session: ACTIVE status, record initialization entry
   - Reset session: archive old (set EndTime, status=ARCHIVED), create new with fresh balance
   - Query sessions: support ACTIVE, ARCHIVED, or ALL statuses

4. **Data Reconciliation**
  - Consume ORDER_TRADE_UPDATE and ACCOUNT_UPDATE forwarded from Executor User Data Stream
  - Ingest through a reliable broker (Redis Streams consumer groups or RabbitMQ via MassTransit)
  - Preserve at-least-once delivery with retry/replay and idempotent deduplication

5. **Real-time Updates**
  - SignalR hub broadcasts ledger entries, balance changes, session updates, markPrice, and open-position snapshots
  - Frontend computes Real-time Equity = Ledger Balance + Unrealized PnL
  - Client-side re-renders triggered by server push

6. **Reset Safety (Saga Pattern)**
  - Emit `Halt_And_Close_All` command to Trading Engine before session reset
  - Wait for `Clear_Confirmed` event (all positions closed, pending orders canceled)
  - Only then archive old session and initialize the new session

### Non-Functional Requirements
- **Precision**: No floating-point loss for financial data (use `decimal`)
- **Idempotency**: All APIs must be idempotent (safe to retry)
- **Testnet/Mainnet parity**: Support both live environments with identical business logic; only API base URL and environment flag differ
- **Scalability**: Support multiple virtual accounts and concurrent sessions
- **Auditability**: Complete transaction history (no deletes)

---

## Architecture

### Service Topology

```
┌─────────────────────────────────────────────────────────────┐
│                    Frontend (React)                          │
│                frontend-ledger:5098                         │
│  ┌──────────────────────────────────────────────────────┐   │
│  │ LedgerPage | EntriesPage | SessionsPage | PnLPage   │   │
│  │              useLedgerSignalR.js                     │   │
│  └──────────────────────────────────────────────────────┘   │
└────────────────────────┬────────────────────────────────────┘
                         │ SignalR + REST
                         ▼
┌─────────────────────────────────────────────────────────────┐
│            FinancialLedger.Worker (Backend)                  │
│                 5097 (HTTP/REST)                            │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  REST Endpoints: /api/ledger/*                       │   │
│  │  SignalR Hub: LedgerHub (real-time pushes)           │   │
│  │  Workers:                                             │   │
│  │  - TradeEventConsumerWorker (durable broker consumer)│   │
│  │  - EquityProjectionWorker (markPrice/open positions) │   │
│  └──────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │ Services:                                             │   │
│  │ - SessionManagementService                           │   │
│  │ - PnlCalculationService                              │   │
│  │ - LedgerRepository (INSERT-only)                     │   │
│  │ - VirtualAccountRepository                           │   │
│  └──────────────────────────────────────────────────────┘   │
└──────┬──────────┬──────────────────────┬────────────────────┘
       │          │                      │
       ▼          ▼                      ▼
  PostgreSQL  Reliable Broker        Executor Websocket
  (Ledger)    (Streams/RabbitMQ)     (User Data Stream)
     │          │                      │
     ├─►┌──────────────────────┐◄─────┤
     │  │ Executor Service     │      │
     │  │ (Order fills) ──────►│      │
     │  └──────────────────────┘      │
     │                                 │
     └─────► Integration Points ◄─────┘
```

### Communication Patterns

| Protocol | Direction | Purpose | Example |
|----------|-----------|---------|---------|
| **REST** | Client → Service | Queries, commands | GET /api/ledger/entries |
| **SignalR** | Service → Client | Real-time pushes | Equity update (balance + unrealized PnL) |
| **Reliable Broker** | Service ↔ Service | Durable event transport | `ledger-events`, `session-reset-saga` |
| **Executor WebSocket** | Exchange → Executor | Source of trade/account events | ORDER_TRADE_UPDATE, ACCOUNT_UPDATE |
| **MassTransit/RabbitMQ** | Service ↔ Service | Saga orchestration + retries | `Halt_And_Close_All`, `Clear_Confirmed` |

---

## Implementation Plan

### Phase 1: Backend Architecture (1-2 days)

#### 1.1 Project Structure
**Location**: `src/Services/FinancialLedger/FinancialLedger.Worker/`

```
FinancialLedger.Worker/
├── Program.cs                          # Entry point, SignalR hub registration, REST routes
├── FinancialLedger.Worker.csproj       # .NET 10.0, nullable enabled, file-scoped namespaces
├── appsettings.json                    # Configuration
├── Hubs/
│   └── LedgerHub.cs                    # SignalR: ReceiveLedgerEntry, ReceiveBalanceUpdate, ReceiveSessionUpdate
├── Workers/
│   ├── TradeEventConsumerWorker.cs     # Consume durable events from broker
│   └── EquityProjectionWorker.cs       # Compute unrealized PnL/equity from live streams
├── Services/
│   ├── SessionManagementService.cs     # Create, reset, archive sessions
│   ├── SessionResetSagaService.cs      # Halt/close/confirm distributed reset flow
│   └── PnlCalculationService.cs        # Calculate balance, net PnL, ROE
├── Infrastructure/
│   ├── LedgerRepository.cs             # LedgerEntry CRUD (INSERT-only)
│   └── VirtualAccountRepository.cs     # VirtualAccount queries
└── Configuration/
    └── LedgerSettings.cs               # Settings binding (sync interval, batch size, etc.)
```

**NuGet Dependencies** (add to `Directory.Packages.props`):
- `Microsoft.AspNetCore.SignalR.Core`
- `MassTransit`
- `MassTransit.RabbitMQ`
- `StackExchange.Redis` (already in project)
- `Dapper` (already in project)
- `Npgsql` (already in project)

#### 1.2 Database Schema Migration
**File**: `scripts/add-ledger-schema.sql`

```sql
-- Virtual Accounts Table
CREATE TABLE virtual_accounts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    environment VARCHAR(20) NOT NULL CHECK (environment IN ('MAINNET', 'TESTNET')),
    base_currency VARCHAR(10) NOT NULL DEFAULT 'USDT',
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(environment, base_currency)
);

-- Test Sessions Table
CREATE TABLE test_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    account_id UUID NOT NULL REFERENCES virtual_accounts(id) ON DELETE CASCADE,
    algorithm_name VARCHAR(255) NOT NULL,
    initial_balance DECIMAL(20, 8) NOT NULL,
    start_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    end_time TIMESTAMP,
    status VARCHAR(20) NOT NULL CHECK (status IN ('ACTIVE', 'ARCHIVED')) DEFAULT 'ACTIVE',
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Ledger Entries Table (INSERT-only, immutable)
CREATE TABLE ledger_entries (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES test_sessions(id) ON DELETE CASCADE,
    binance_transaction_id VARCHAR(255),
    type VARCHAR(50) NOT NULL CHECK (type IN (
        'INITIAL_FUNDING', 'REALIZED_PNL', 'COMMISSION', 'FUNDING_FEE', 'WITHDRAWAL'
    )),
    amount DECIMAL(20, 8) NOT NULL,
    symbol VARCHAR(20),
    timestamp TIMESTAMP NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(session_id, binance_transaction_id) WHERE binance_transaction_id IS NOT NULL
);

-- Indexes for performance
CREATE INDEX idx_ledger_entries_session_id ON ledger_entries(session_id);
CREATE INDEX idx_ledger_entries_session_timestamp ON ledger_entries(session_id, timestamp);
CREATE INDEX idx_ledger_entries_symbol ON ledger_entries(symbol);
CREATE INDEX idx_ledger_entries_type ON ledger_entries(type);
CREATE INDEX idx_test_sessions_account_id ON test_sessions(account_id);
CREATE INDEX idx_test_sessions_status ON test_sessions(status);

-- Trigger to prevent direct updates on ledger_entries (enforce INSERT-only)
CREATE OR REPLACE FUNCTION prevent_ledger_updates()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'Updates to ledger_entries are not allowed. Ledger is immutable.';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER ledger_entries_no_update
BEFORE UPDATE ON ledger_entries
FOR EACH ROW
EXECUTE FUNCTION prevent_ledger_updates();
```

**Run migration**:
```bash
psql -h localhost -U trader -d cryptotrading -f scripts/add-ledger-schema.sql
```

#### 1.3 Program.cs & Configuration

**File**: `src/Services/FinancialLedger/FinancialLedger.Worker/Program.cs`

```csharp
using FinancialLedger.Worker.Hubs;
using FinancialLedger.Worker.Configuration;
using FinancialLedger.Worker.Infrastructure;
using FinancialLedger.Worker.Services;
using FinancialLedger.Worker.Workers;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// gRPC HTTP/2 unencrypted support
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

// Configuration
var ledgerSettings = new LedgerSettings();
builder.Configuration.GetSection("Ledger").Bind(ledgerSettings);
builder.Services.AddSingleton(ledgerSettings);

// Database
var connectionString = builder.Configuration.GetConnectionString("Postgres");
builder.Services.AddScoped<LedgerRepository>();
builder.Services.AddScoped<VirtualAccountRepository>();

// Redis
var redisConnStr = builder.Configuration.GetConnectionString("Redis");
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnStr));

// Services
builder.Services.AddScoped<SessionManagementService>();
builder.Services.AddScoped<PnlCalculationService>();
builder.Services.AddScoped<SessionResetSagaService>();
builder.Services.AddSingleton<TradeEventConsumerWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TradeEventConsumerWorker>());
builder.Services.AddSingleton<EquityProjectionWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EquityProjectionWorker>());

// SignalR
builder.Services.AddSignalR();

// HTTP clients
builder.Services.AddHttpClient<BinanceHttpClient>();

// Logging
builder.Services.AddLogging(config => config.AddConsole());

var app = builder.Build();

// Middleware
app.UseRouting();
app.UseWebSockets();

// SignalR Hub
app.MapHub<LedgerHub>("/ledger-hub");

// REST Endpoints
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "FinancialLedger" }));

// Ledger API
app.MapGet("/api/ledger/account/{accountId}", GetAccountAsync);
app.MapGet("/api/ledger/entries", GetEntriesAsync);
app.MapGet("/api/ledger/sessions/{accountId}", GetSessionsAsync);
app.MapGet("/api/ledger/pnl", GetPnlAsync);
app.MapPost("/api/ledger/sessions/reset", ResetSessionAsync);

await app.RunAsync();

// Endpoint handlers (implement below)
async Task<IResult> GetAccountAsync(string accountId, IServiceProvider sp) { /* ... */ }
async Task<IResult> GetEntriesAsync(IServiceProvider sp) { /* ... */ }
async Task<IResult> GetSessionsAsync(string accountId, IServiceProvider sp) { /* ... */ }
async Task<IResult> GetPnlAsync(IServiceProvider sp) { /* ... */ }
async Task<IResult> ResetSessionAsync(ResetSessionRequest req, IServiceProvider sp) { /* ... */ }
```

**File**: `src/Services/FinancialLedger/FinancialLedger.Worker/Configuration/LedgerSettings.cs`

```csharp
namespace FinancialLedger.Worker.Configuration;

public class LedgerSettings
{
    public int BinanceSyncIntervalMinutes { get; set; } = 5;
    public int BatchSize { get; set; } = 100;
    public string RedisPrefix { get; set; } = "ledger";
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;
}
```

---

### Phase 2: Core Services (2-3 days)

#### 2.1 Ledger Repository
**File**: `src/Services/FinancialLedger/FinancialLedger.Worker/Infrastructure/LedgerRepository.cs`

```csharp
using Dapper;
using Npgsql;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FinancialLedger.Worker.Infrastructure;

public class LedgerRepository
{
    private readonly string _connectionString;
    private readonly ILogger<LedgerRepository> _logger;

    public LedgerRepository(IConfiguration config, ILogger<LedgerRepository> logger)
    {
        _connectionString = config.GetConnectionString("Postgres")!;
        _logger = logger;
    }

    /// <summary>
    /// Idempotent insert: checks for duplicate binance_transaction_id before inserting
    /// </summary>
    public async Task<bool> InsertLedgerEntryAsync(
        Guid sessionId,
        string? binanceTransactionId,
        string type,
        decimal amount,
        string? symbol,
        DateTime timestamp)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // Idempotency check
        if (!string.IsNullOrEmpty(binanceTransactionId))
        {
            var existing = await connection.QuerySingleOrDefaultAsync<dynamic>(
                "SELECT id FROM ledger_entries WHERE session_id = @SessionId AND binance_transaction_id = @TranId",
                new { SessionId = sessionId, TranId = binanceTransactionId });

            if (existing != null)
            {
                _logger.LogInformation($"Duplicate entry skipped: {binanceTransactionId}");
                return false; // Already exists
            }
        }

        // Insert
        var result = await connection.ExecuteAsync(
            """
            INSERT INTO ledger_entries (session_id, binance_transaction_id, type, amount, symbol, timestamp)
            VALUES (@SessionId, @BinanceTranId, @Type, @Amount, @Symbol, @Timestamp)
            """,
            new
            {
                SessionId = sessionId,
                BinanceTranId = binanceTransactionId,
                Type = type,
                Amount = amount,
                Symbol = symbol,
                Timestamp = timestamp
            });

        return result > 0;
    }

    /// <summary>
    /// Get current balance for session: SUM all amounts
    /// </summary>
    public async Task<decimal> GetCurrentBalanceAsync(Guid sessionId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var balance = await connection.QuerySingleAsync<decimal>(
            "SELECT COALESCE(SUM(amount), 0) FROM ledger_entries WHERE session_id = @SessionId",
            new { SessionId = sessionId });
        return balance;
    }

    /// <summary>
    /// Get paginated ledger entries with optional filters
    /// </summary>
    public async Task<(List<LedgerEntryDto> Entries, int Total)> GetLedgerEntriesAsync(
        Guid sessionId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? symbol = null,
        string? type = null,
        int page = 1,
        int pageSize = 50)
    {
        using var connection = new NpgsqlConnection(_connectionString);

        var whereClause = "WHERE session_id = @SessionId";
        var parameters = new { SessionId = sessionId, FromDate = fromDate, ToDate = toDate, Symbol = symbol, Type = type };

        if (fromDate.HasValue) whereClause += " AND timestamp >= @FromDate";
        if (toDate.HasValue) whereClause += " AND timestamp <= @ToDate";
        if (!string.IsNullOrEmpty(symbol)) whereClause += " AND symbol = @Symbol";
        if (!string.IsNullOrEmpty(type)) whereClause += " AND type = @Type";

        var total = await connection.QuerySingleAsync<int>(
            $"SELECT COUNT(*) FROM ledger_entries {whereClause}",
            parameters);

        var entries = (await connection.QueryAsync<LedgerEntryDto>(
            $"""
            SELECT id, session_id as SessionId, binance_transaction_id as BinanceTransactionId, 
                   type as Type, amount as Amount, symbol as Symbol, timestamp as Timestamp
            FROM ledger_entries {whereClause}
            ORDER BY timestamp DESC
            LIMIT @PageSize OFFSET @Offset
            """,
            new { pageSize, offset = (page - 1) * pageSize, ...parameters.ToExpando() })).ToList();

        return (entries, total);
    }

    /// <summary>
    /// Get net P&L breakdown by symbol: sum(realized_pnl) + sum(commission) + sum(funding_fee)
    /// </summary>
    public async Task<Dictionary<string, PnlBreakdown>> GetPnlBySymbolAsync(Guid sessionId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var results = await connection.QueryAsync<dynamic>(
            """
            SELECT 
                symbol,
                SUM(CASE WHEN type = 'REALIZED_PNL' THEN amount ELSE 0 END) as realized_pnl,
                SUM(CASE WHEN type = 'COMMISSION' THEN amount ELSE 0 END) as commission,
                SUM(CASE WHEN type = 'FUNDING_FEE' THEN amount ELSE 0 END) as funding_fee
            FROM ledger_entries
            WHERE session_id = @SessionId AND symbol IS NOT NULL
            GROUP BY symbol
            """,
            new { SessionId = sessionId });

        return results.ToDictionary(
            r => (string)r.symbol,
            r => new PnlBreakdown
            {
                RealizedPnL = (decimal)r.realized_pnl,
                Commission = (decimal)r.commission,
                FundingFee = (decimal)r.funding_fee,
                NetPnL = (decimal)(r.realized_pnl + r.commission + r.funding_fee)
            });
    }
}

public record LedgerEntryDto(
    Guid Id,
    Guid SessionId,
    string? BinanceTransactionId,
    string Type,
    decimal Amount,
    string? Symbol,
    DateTime Timestamp);

public record PnlBreakdown(
    decimal RealizedPnL,
    decimal Commission,
    decimal FundingFee,
    decimal NetPnL);
```

#### 2.2 Virtual Account Repository
**File**: `src/Services/FinancialLedger/FinancialLedger.Worker/Infrastructure/VirtualAccountRepository.cs`

```csharp
namespace FinancialLedger.Worker.Infrastructure;

public class VirtualAccountRepository
{
    private readonly string _connectionString;

    public VirtualAccountRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Postgres")!;
    }

    /// <summary>
    /// Get or create a virtual account for the given environment
    /// </summary>
    public async Task<Guid> GetOrCreateAccountAsync(string environment, string baseCurrency = "USDT")
    {
        using var connection = new NpgsqlConnection(_connectionString);
        
        // Check if exists
        var existing = await connection.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM virtual_accounts WHERE environment = @Env AND base_currency = @Currency",
            new { Env = environment, Currency = baseCurrency });

        if (existing.HasValue)
            return existing.Value;

        // Create new
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO virtual_accounts (id, environment, base_currency) VALUES (@Id, @Env, @Currency)",
            new { Id = id, Env = environment, Currency = baseCurrency });

        return id;
    }

    /// <summary>
    /// Get account details
    /// </summary>
    public async Task<VirtualAccountDto?> GetAccountAsync(Guid accountId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<VirtualAccountDto>(
            "SELECT id as Id, environment as Environment, base_currency as BaseCurrency, created_at as CreatedAt FROM virtual_accounts WHERE id = @Id",
            new { Id = accountId });
    }
}

public record VirtualAccountDto(Guid Id, string Environment, string BaseCurrency, DateTime CreatedAt);
```

#### 2.3 Session Management Service
**File**: `src/Services/FinancialLedger/FinancialLedger.Worker/Services/SessionManagementService.cs`

```csharp
namespace FinancialLedger.Worker.Services;

public class SessionManagementService
{
    private readonly string _connectionString;
    private readonly ILogger<SessionManagementService> _logger;
    private readonly IConnectionMultiplexer _redis;

    public SessionManagementService(IConfiguration config, ILogger<SessionManagementService> logger, IConnectionMultiplexer redis)
    {
        _connectionString = config.GetConnectionString("Postgres")!;
        _logger = logger;
        _redis = redis;
    }

    /// <summary>
    /// Create a new ACTIVE session with initial funding
    /// </summary>
    public async Task<Guid> CreateSessionAsync(Guid accountId, string algorithmName, decimal initialBalance)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = await connection.BeginTransactionAsync();

        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Insert session
        await connection.ExecuteAsync(
            """
            INSERT INTO test_sessions (id, account_id, algorithm_name, initial_balance, start_time, status)
            VALUES (@Id, @AccountId, @AlgoName, @InitialBalance, @StartTime, 'ACTIVE')
            """,
            new { Id = sessionId, AccountId = accountId, AlgoName = algorithmName, InitialBalance = initialBalance, StartTime = now },
            transaction);

        // Insert initial funding entry
        await connection.ExecuteAsync(
            """
            INSERT INTO ledger_entries (session_id, type, amount, timestamp)
            VALUES (@SessionId, 'INITIAL_FUNDING', @Amount, @Timestamp)
            """,
            new { SessionId = sessionId, Amount = initialBalance, Timestamp = now },
            transaction);

        await transaction.CommitAsync();

        _logger.LogInformation($"Session created: {sessionId} with balance {initialBalance}");

        // Publish event
        var pub = _redis.GetSubscriber();
        await pub.PublishAsync("session:created", sessionId.ToString());

        return sessionId;
    }

    /// <summary>
    /// Reset session: archive old, create new
    /// </summary>
    public async Task<Guid> ResetSessionAsync(Guid accountId, decimal newInitialBalance, string algorithmName)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = await connection.BeginTransactionAsync();

        var now = DateTime.UtcNow;

        // Archive old ACTIVE session
        await connection.ExecuteAsync(
            """
            UPDATE test_sessions 
            SET status = 'ARCHIVED', end_time = @EndTime, updated_at = @UpdatedAt
            WHERE account_id = @AccountId AND status = 'ACTIVE'
            """,
            new { AccountId = accountId, EndTime = now, UpdatedAt = now },
            transaction);

        // Create new session
        var newSessionId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO test_sessions (id, account_id, algorithm_name, initial_balance, start_time, status)
            VALUES (@Id, @AccountId, @AlgoName, @InitialBalance, @StartTime, 'ACTIVE')
            """,
            new { Id = newSessionId, AccountId = accountId, AlgoName = algorithmName, InitialBalance = newInitialBalance, StartTime = now },
            transaction);

        // Insert initial funding for new session
        await connection.ExecuteAsync(
            """
            INSERT INTO ledger_entries (session_id, type, amount, timestamp)
            VALUES (@SessionId, 'INITIAL_FUNDING', @Amount, @Timestamp)
            """,
            new { SessionId = newSessionId, Amount = newInitialBalance, Timestamp = now },
            transaction);

        await transaction.CommitAsync();

        _logger.LogInformation($"Session reset: new session {newSessionId} with balance {newInitialBalance}");

        // Publish event
        var pub = _redis.GetSubscriber();
        await pub.PublishAsync("session:reset", newSessionId.ToString());

        return newSessionId;
    }

    /// <summary>
    /// Get active session for account
    /// </summary>
    public async Task<SessionDto?> GetActiveSessionAsync(Guid accountId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<SessionDto>(
            """
            SELECT id as Id, account_id as AccountId, algorithm_name as AlgorithmName, 
                   initial_balance as InitialBalance, start_time as StartTime, status as Status
            FROM test_sessions
            WHERE account_id = @AccountId AND status = 'ACTIVE'
            """,
            new { AccountId = accountId });
    }

    /// <summary>
    /// Get all sessions for account
    /// </summary>
    public async Task<List<SessionDto>> GetSessionsAsync(Guid accountId, string? status = null)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var whereClause = "WHERE account_id = @AccountId";
        if (!string.IsNullOrEmpty(status)) whereClause += " AND status = @Status";

        var sessions = (await connection.QueryAsync<SessionDto>(
            $"""
            SELECT id as Id, account_id as AccountId, algorithm_name as AlgorithmName, 
                   initial_balance as InitialBalance, start_time as StartTime, status as Status
            FROM test_sessions {whereClause}
            ORDER BY start_time DESC
            """,
            new { AccountId = accountId, Status = status })).ToList();

        return sessions;
    }
}

public record SessionDto(Guid Id, Guid AccountId, string AlgorithmName, decimal InitialBalance, DateTime StartTime, string Status);
```

#### 2.4 P&L Calculation Service
**File**: `src/Services/FinancialLedger/FinancialLedger.Worker/Services/PnlCalculationService.cs`

```csharp
namespace FinancialLedger.Worker.Services;

public class PnlCalculationService
{
    private readonly LedgerRepository _ledgerRepo;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PnlCalculationService> _logger;

    private const string CachePrefix = "pnl:";
    private const int CacheTtlSeconds = 60;

    public PnlCalculationService(LedgerRepository ledgerRepo, IConnectionMultiplexer redis, ILogger<PnlCalculationService> logger)
    {
        _ledgerRepo = ledgerRepo;
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Get current balance for session (cached)
    /// </summary>
    public async Task<decimal> GetCurrentBalanceAsync(Guid sessionId)
    {
        var cacheKey = $"{CachePrefix}balance:{sessionId}";
        var db = _redis.GetDatabase();

        // Try cache
        var cached = await db.StringGetAsync(cacheKey);
        if (cached.HasValue && decimal.TryParse(cached.ToString(), out var cachedBalance))
        {
            return cachedBalance;
        }

        // Calculate
        var balance = await _ledgerRepo.GetCurrentBalanceAsync(sessionId);

        // Cache
        await db.StringSetAsync(cacheKey, balance.ToString(), TimeSpan.FromSeconds(CacheTtlSeconds));

        return balance;
    }

    /// <summary>
    /// Calculate net P&L: sum(realized_pnl) + sum(commission) + sum(funding_fee)
    /// </summary>
    public async Task<decimal> CalculateNetPnLAsync(Guid sessionId, string? symbol = null)
    {
        var breakdownDict = await _ledgerRepo.GetPnlBySymbolAsync(sessionId);

        if (string.IsNullOrEmpty(symbol))
        {
            // Sum all symbols
            return breakdownDict.Values.Sum(b => b.NetPnL);
        }

        // Single symbol
        return breakdownDict.TryGetValue(symbol, out var breakdown) ? breakdown.NetPnL : 0m;
    }

    /// <summary>
    /// Calculate ROE: (Net PnL / Initial Margin) × 100%
    /// Fallback: If initial margin unknown, use initial_balance
    /// </summary>
    public async Task<decimal> CalculateROEAsync(Guid sessionId)
    {
        var balance = await GetCurrentBalanceAsync(sessionId);
        var netPnL = await CalculateNetPnLAsync(sessionId);

        if (balance == 0m)
            return 0m;

        return (netPnL / balance) * 100m;
    }

    /// <summary>
    /// Invalidate balance cache (call after ledger insert)
    /// </summary>
    public async Task InvalidateCacheAsync(Guid sessionId)
    {
        var db = _redis.GetDatabase();
        var cacheKey = $"{CachePrefix}balance:{sessionId}";
        await db.KeyDeleteAsync(cacheKey);
        _logger.LogInformation($"Cache invalidated for session {sessionId}");
    }
}
```

#### 2.5 Trade Event Consumer Worker
**File**: `src/Services/FinancialLedger/FinancialLedger.Worker/Workers/TradeEventConsumerWorker.cs`

```csharp
namespace FinancialLedger.Worker.Workers;

public class TradeEventConsumerWorker : BackgroundService
{
  private readonly ILogger<TradeEventConsumerWorker> _logger;
    private readonly LedgerSettings _settings;
    private readonly LedgerRepository _ledgerRepo;
    private readonly SessionManagementService _sessionService;
    private readonly PnlCalculationService _pnlService;
  private readonly IEventConsumer _eventConsumer;

  public TradeEventConsumerWorker(
    ILogger<TradeEventConsumerWorker> logger,
        LedgerSettings settings,
        LedgerRepository ledgerRepo,
        SessionManagementService sessionService,
        PnlCalculationService pnlService,
    IEventConsumer eventConsumer)
    {
        _logger = logger;
        _settings = settings;
        _ledgerRepo = ledgerRepo;
        _sessionService = sessionService;
        _pnlService = pnlService;
    _eventConsumer = eventConsumer;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
    _logger.LogInformation("TradeEventConsumerWorker started");

        while (!ct.IsCancellationRequested)
        {
            try
            {
        var evt = await _eventConsumer.ConsumeAsync(ct);
        // evt source: Executor User Data Stream bridge (ORDER_TRADE_UPDATE / ACCOUNT_UPDATE)
        // Delivery semantics: durable queue/stream with acknowledgement and retry
        // 1) map event to ledger entry type
        // 2) apply idempotent insert by transaction/event key
        // 3) invalidate projections and publish SignalR update
        // 4) ack only after successful persistence
            }
            catch (Exception ex)
            {
        _logger.LogError(ex, "Error while consuming trade/account events");
                await Task.Delay(TimeSpan.FromSeconds(30), ct); // Brief delay before retry
            }
        }
    }
}
```

#### 2.6 SignalR Hub
**File**: `src/Services/FinancialLedger/FinancialLedger.Worker/Hubs/LedgerHub.cs`

```csharp
using Microsoft.AspNetCore.SignalR;

namespace FinancialLedger.Worker.Hubs;

public class LedgerHub : Hub
{
    private readonly ILogger<LedgerHub> _logger;

    public LedgerHub(ILogger<LedgerHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation($"Client connected: {Context.ConnectionId}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Server sends ledger entry to clients
    /// </summary>
    public async Task SendLedgerEntryAsync(object entry)
    {
        await Clients.All.SendAsync("ReceiveLedgerEntry", entry);
    }

    /// <summary>
    /// Server sends balance update to clients
    /// </summary>
    public async Task SendBalanceUpdateAsync(object balanceData)
    {
        await Clients.All.SendAsync("ReceiveBalanceUpdate", balanceData);
    }

    /// <summary>
    /// Server sends session update to clients
    /// </summary>
    public async Task SendSessionUpdateAsync(object sessionData)
    {
        await Clients.All.SendAsync("ReceiveSessionUpdate", sessionData);
    }
}
```

---

### Phase 3: REST API Endpoints (1 day)

**File**: `src/Services/FinancialLedger/FinancialLedger.Worker/Program.cs` (extend)

```csharp
// GET /api/ledger/account/{accountId}
app.MapGet("/api/ledger/account/{accountId}", async (
    string accountId,
    LedgerRepository ledgerRepo,
    PnlCalculationService pnlService,
    SessionManagementService sessionService) =>
{
    if (!Guid.TryParse(accountId, out var acctId)) return Results.BadRequest("Invalid account ID");

    var session = await sessionService.GetActiveSessionAsync(acctId);
    if (session == null) return Results.NotFound("No active session");

    var balance = await pnlService.GetCurrentBalanceAsync(session.Id);
    var netPnL = await pnlService.CalculateNetPnLAsync(session.Id);
    var roe = await pnlService.CalculateROEAsync(session.Id);

    return Results.Ok(new
    {
        session.Id,
        session.InitialBalance,
        CurrentBalance = balance,
        NetPnL = netPnL,
        ROE_Percent = roe
    });
})
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.WithName("GetAccount")
.WithOpenApi();

// GET /api/ledger/entries
app.MapGet("/api/ledger/entries", async (
    [FromQuery] string sessionId,
    [FromQuery] DateTime? fromDate,
    [FromQuery] DateTime? toDate,
    [FromQuery] string? symbol,
    [FromQuery] string? type,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50,
    LedgerRepository ledgerRepo) =>
{
    if (!Guid.TryParse(sessionId, out var sessId)) return Results.BadRequest("Invalid session ID");

    var (entries, total) = await ledgerRepo.GetLedgerEntriesAsync(
        sessId, fromDate, toDate, symbol, type, page, pageSize);

    return Results.Ok(new { entries, total, page, pageSize });
})
.Produces(StatusCodes.Status200OK)
.WithName("GetEntries")
.WithOpenApi();

// GET /api/ledger/sessions/{accountId}
app.MapGet("/api/ledger/sessions/{accountId}", async (
    string accountId,
    [FromQuery] string? status,
    SessionManagementService sessionService) =>
{
    if (!Guid.TryParse(accountId, out var acctId)) return Results.BadRequest("Invalid account ID");

    var sessions = await sessionService.GetSessionsAsync(acctId, status);
    return Results.Ok(sessions);
})
.Produces(StatusCodes.Status200OK)
.WithName("GetSessions")
.WithOpenApi();

// GET /api/ledger/pnl
app.MapGet("/api/ledger/pnl", async (
    [FromQuery] string sessionId,
    [FromQuery] string? symbol,
    PnlCalculationService pnlService,
    LedgerRepository ledgerRepo) =>
{
    if (!Guid.TryParse(sessionId, out var sessId)) return Results.BadRequest("Invalid session ID");

    var breakdown = await ledgerRepo.GetPnlBySymbolAsync(sessId);

    if (!string.IsNullOrEmpty(symbol))
    {
        var filtered = breakdown.Where(kvp => kvp.Key == symbol).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        return Results.Ok(filtered);
    }

    return Results.Ok(breakdown);
})
.Produces(StatusCodes.Status200OK)
.WithName("GetPnL")
.WithOpenApi();

// POST /api/ledger/sessions/reset
app.MapPost("/api/ledger/sessions/reset", async (
    ResetSessionRequest req,
    SessionManagementService sessionService) =>
{
    if (!Guid.TryParse(req.AccountId, out var acctId)) 
        return Results.BadRequest("Invalid account ID");
    if (req.NewInitialBalance <= 0) 
        return Results.BadRequest("Initial balance must be positive");

    var newSessionId = await sessionService.ResetSessionAsync(
        acctId, req.NewInitialBalance, req.AlgorithmName);

    return Results.CreatedAtRoute("GetAccount", new { accountId = acctId }, new { newSessionId });
})
.Produces(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest)
.WithName("ResetSession")
.WithOpenApi();

public record ResetSessionRequest(string AccountId, decimal NewInitialBalance, string AlgorithmName);
```

---

### Phase 4: Integration (1-2 days)

#### 4.1 Executor Service Updates
Modify Executor event pipeline:
- Forward ORDER_TRADE_UPDATE and ACCOUNT_UPDATE from User Data Stream to durable broker topics/queues
- Publish markPrice and open-position snapshots for unrealized PnL projection
- Guarantee retry + dead-letter handling so Ledger can catch up after downtime

#### 4.2 Shared Contracts + Broker/Saga Updates
Add shared message contracts:
- `OrderTradeUpdatedEvent`
- `AccountUpdatedEvent`
- `MarkPriceUpdatedEvent`
- `HaltAndCloseAllCommand`
- `ClearConfirmedEvent`
- `SessionResetRequestedEvent`

Replace fire-and-forget pub/sub constants with durable names:
```csharp
public static class LedgerEventNames
{
    public const string LedgerEventsStream = "ledger:events";
    public const string SessionResetSaga = "session-reset-saga";
    public const string TradingEngineCommands = "trading-engine:commands";
}
```

#### 4.3 Session Reset Saga
- Implement reset as distributed transaction:
  1. Ledger emits `Halt_And_Close_All`
  2. Trading Engine performs close-all and cancel-all
  3. Trading Engine emits `Clear_Confirmed`
  4. Ledger archives old session and creates new session with INITIAL_FUNDING
- Add timeout and compensation logic when confirmation is delayed/missing

---

### Phase 5: Frontend Dashboard (2-3 days)

#### 5.1 Project Setup
```bash
cd frontend-ledger
npm create vite@latest . -- --template react
npm install
npm install @microsoft/signalr recharts react-query react-i18next i18next
```

**Vite config** (`vite.config.js`):
```javascript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5098,
    proxy: {
      '/api': 'http://localhost:5097',
      '/ledger-hub': {
        target: 'ws://localhost:5097',
        ws: true
      }
    }
  }
})
```

#### 5.2 SignalR Hook
**File**: `frontend-ledger/src/hooks/useLedgerSignalR.js`

```javascript
import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';

export const useLedgerSignalR = () => {
  const connectionRef = useRef(null);
  const [isConnected, setIsConnected] = useState(false);

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/ledger-hub')
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .build();

    connection.on('ReceiveLedgerEntry', (entry) => {
      console.log('New ledger entry:', entry);
      // Dispatch Redux/Context action
    });

    connection.on('ReceiveBalanceUpdate', (balanceData) => {
      console.log('Balance updated:', balanceData);
    });

    connection.on('ReceiveSessionUpdate', (sessionData) => {
      console.log('Session updated:', sessionData);
    });

    connection.onreconnected(() => console.log('SignalR reconnected'));
    connection.onreconnecting((error) => console.warn('SignalR reconnecting:', error));
    connection.onclose((error) => {
      console.error('SignalR connection closed:', error);
      setIsConnected(false);
    });

    connection.start()
      .then(() => {
        console.log('SignalR connected');
        setIsConnected(true);
      })
      .catch(err => console.error('SignalR connection error:', err));

    connectionRef.current = connection;

    return () => connection.stop();
  }, []);

  return { isConnected };
};
```

#### 5.3 API Service
**File**: `frontend-ledger/src/services/ledgerApi.js`

```javascript
const API_URL = import.meta.env.VITE_API_URL || 'http://localhost:5097';

export const ledgerApi = {
  getAccount: async (accountId) => {
    const res = await fetch(`${API_URL}/api/ledger/account/${accountId}`);
    if (!res.ok) throw new Error(`Failed to fetch account: ${res.status}`);
    return res.json();
  },

  getEntries: async (sessionId, { fromDate, toDate, symbol, type, page = 1, pageSize = 50 } = {}) => {
    const params = new URLSearchParams({ sessionId, page, pageSize });
    if (fromDate) params.append('fromDate', fromDate);
    if (toDate) params.append('toDate', toDate);
    if (symbol) params.append('symbol', symbol);
    if (type) params.append('type', type);

    const res = await fetch(`${API_URL}/api/ledger/entries?${params}`);
    if (!res.ok) throw new Error(`Failed to fetch entries: ${res.status}`);
    return res.json();
  },

  getSessions: async (accountId, status) => {
    const params = new URLSearchParams({ status: status || 'ALL' });
    const res = await fetch(`${API_URL}/api/ledger/sessions/${accountId}?${params}`);
    if (!res.ok) throw new Error(`Failed to fetch sessions: ${res.status}`);
    return res.json();
  },

  getPnL: async (sessionId, symbol) => {
    const params = new URLSearchParams({ sessionId });
    if (symbol) params.append('symbol', symbol);

    const res = await fetch(`${API_URL}/api/ledger/pnl?${params}`);
    if (!res.ok) throw new Error(`Failed to fetch P&L: ${res.status}`);
    return res.json();
  },

  resetSession: async (accountId, newInitialBalance, algorithmName) => {
    const res = await fetch(`${API_URL}/api/ledger/sessions/reset`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ accountId, newInitialBalance, algorithmName })
    });
    if (!res.ok) throw new Error(`Failed to reset session: ${res.status}`);
    return res.json();
  }
};
```

#### 5.4 React Pages

**File**: `frontend-ledger/src/pages/LedgerPage.jsx`

```javascript
import React, { useEffect, useState } from 'react';
import { useLedgerSignalR } from '../hooks/useLedgerSignalR';
import { ledgerApi } from '../services/ledgerApi';

export const LedgerPage = ({ accountId }) => {
  const [account, setAccount] = useState(null);
  const [loading, setLoading] = useState(true);
  const { isConnected } = useLedgerSignalR();

  useEffect(() => {
    const fetchAccount = async () => {
      try {
        const data = await ledgerApi.getAccount(accountId);
        setAccount(data);
      } catch (err) {
        console.error('Failed to fetch account:', err);
      } finally {
        setLoading(false);
      }
    };

    fetchAccount();
  }, [accountId]);

  if (loading) return <div>Loading...</div>;
  if (!account) return <div>No active session</div>;

  return (
    <div className="p-6">
      <h1 className="text-3xl font-bold mb-4">Ledger Dashboard</h1>
      <div className="grid grid-cols-3 gap-4">
        <div className="bg-white p-4 rounded shadow">
          <h2 className="text-gray-600">Initial Balance</h2>
          <p className="text-2xl font-bold">${account.initialBalance.toFixed(2)}</p>
        </div>
        <div className="bg-white p-4 rounded shadow">
          <h2 className="text-gray-600">Current Balance</h2>
          <p className="text-2xl font-bold">${account.currentBalance.toFixed(2)}</p>
        </div>
        <div className={`bg-white p-4 rounded shadow ${account.netPnL >= 0 ? 'border-l-4 border-green-500' : 'border-l-4 border-red-500'}`}>
          <h2 className="text-gray-600">Net P&L</h2>
          <p className={`text-2xl font-bold ${account.netPnL >= 0 ? 'text-green-600' : 'text-red-600'}`}>
            ${account.netPnL.toFixed(2)}
          </p>
        </div>
      </div>
      <div className="mt-4 bg-blue-50 p-2 rounded">
        {isConnected ? <span className="text-green-600">✓ Live</span> : <span className="text-red-600">✗ Offline</span>}
      </div>
    </div>
  );
};
```

**Additional pages**: `EntriesPage.jsx`, `SessionsPage.jsx`, `PnLBreakdownPage.jsx` (similar structure)

#### 5.5 i18n Setup
**File**: `frontend-ledger/src/i18n/locales/vi/ledger.json`

```json
{
  "title": "Sổ cái & Báo cáo",
  "balance": "Số dư",
  "netPnL": "Lợi nhuận ròng",
  "entries": "Mục nhập sổ cái",
  "sessions": "Phiên giao dịch",
  "currentBalance": "Số dư hiện tại",
  "initialBalance": "Số dư ban đầu",
  "live": "Trực tiếp",
  "offline": "Ngoại tuyến"
}
```

---

### Phase 6: Docker & Deployment (0.5 days)

#### 6.1 Dockerfile
**File**: `src/Services/FinancialLedger/FinancialLedger.Worker/Dockerfile`

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
COPY ["src/Services/FinancialLedger/FinancialLedger.Worker/FinancialLedger.Worker.csproj", "src/Services/FinancialLedger/FinancialLedger.Worker/"]

RUN dotnet restore "src/Services/FinancialLedger/FinancialLedger.Worker/FinancialLedger.Worker.csproj"

COPY . .
RUN dotnet publish "src/Services/FinancialLedger/FinancialLedger.Worker/FinancialLedger.Worker.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5097
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s CMD curl -f http://localhost:5097/health || exit 1

ENTRYPOINT ["dotnet", "FinancialLedger.Worker.dll"]
```

#### 6.2 Docker Compose
Add to `infrastructure/docker-compose.yml`:

```yaml
financialledger:
  build:
    context: ..
    dockerfile: src/Services/FinancialLedger/FinancialLedger.Worker/Dockerfile
  ports:
    - "5097:5097"
  environment:
    - ASPNETCORE_ENVIRONMENT=Development
    - ConnectionStrings__Postgres=Host=postgres;Port=5432;Username=trader;Password=strongpassword;Database=cryptotrading
    - ConnectionStrings__Redis=redis:6379
    - Binance__ApiKey=${BINANCE_API_KEY}
    - Binance__ApiSecret=${BINANCE_API_SECRET}
    - Ledger__BinanceSyncIntervalMinutes=5
  depends_on:
    postgres:
      condition: service_healthy
    redis:
      condition: service_healthy
  healthcheck:
    test: ["CMD", "curl", "-f", "http://localhost:5097/health"]
    interval: 30s
    timeout: 10s
    retries: 3

frontend-ledger:
  build:
    context: ../frontend-ledger
    dockerfile: Dockerfile
  ports:
    - "5098:5173"
  environment:
    - VITE_API_URL=http://financialledger:5097
    - VITE_SIGNALR_HUB_URL=ws://localhost:5097/ledger-hub
  depends_on:
    - financialledger
```

#### 6.3 Environment File
Add to `infrastructure/.env.template`:

```env
# FinancialLedger Service
FINANCIAL_LEDGER_PORT=5097
FINANCIAL_LEDGER_BINANCE_SYNC_INTERVAL_MINUTES=5
FINANCIAL_LEDGER_REDIS_PREFIX=ledger

# Frontend
FRONTEND_LEDGER_PORT=5098
VITE_API_URL=http://localhost:5097
VITE_SIGNALR_HUB_URL=ws://localhost:5097/ledger-hub
```

---

## Verification Plan

### Phase 1-3 Verification
```bash
# 1. Build service
cd src/Services/FinancialLedger/FinancialLedger.Worker
dotnet build

# 2. Run migrations
psql -h localhost -U trader -d cryptotrading -f scripts/add-ledger-schema.sql

# 3. Verify schema
psql -h localhost -U trader -d cryptotrading -c "\d ledger_entries"

# 4. Start service (local)
dotnet run --configuration Release

# 5. Test endpoints
curl http://localhost:5097/health
curl http://localhost:5097/api/ledger/account/{accountId}
```

### Phase 5 Verification
```bash
# 1. Build frontend
cd frontend-ledger
npm run build

# 2. Dev server
npm run dev

# 3. Load http://localhost:5098
# Verify dashboard renders, SignalR connects, data loads
```

### Full Integration Test
```bash
# 1. Start docker-compose
cd infrastructure
docker-compose up -d

# 2. Check health
curl http://localhost:5097/health
curl http://localhost:5098 (frontend loads)

# 3. Create session
curl -X POST http://localhost:5097/api/ledger/sessions/reset \
  -H "Content-Type: application/json" \
  -d '{"accountId": "550e8400-e29b-41d4-a716-446655440000", "newInitialBalance": 10000, "algorithmName": "RSI_EMA"}'

# 4. Get account balance
curl http://localhost:5097/api/ledger/account/550e8400-e29b-41d4-a716-446655440000

# 5. Verify real-time updates via SignalR
# (Open DevTools in frontend, check console for SignalR messages)
```

---

## Timeline & Resources

| Phase | Duration | Key Deliverables | Owner |
|-------|----------|------------------|-------|
| 1 | 1-2 days | Project structure, DB schema, Program.cs | Backend Dev |
| 2 | 2-3 days | Ledger/Account repos, Services, Workers, SignalR hub | Backend Dev |
| 3 | 1 day | REST API endpoints (5x GET/POST) | Backend Dev |
| 4 | 1-2 days | Executor integration, Shared library updates | Backend Dev + Executor Dev |
| 5 | 2-3 days | React pages, SignalR hook, API client, i18n | Frontend Dev |
| 6 | 0.5 days | Dockerfile, docker-compose, .env | DevOps |
| **Total** | **7-12 days** | Full backend + frontend service | Team |

---

## Key Decisions

| Decision | Rationale |
|----------|-----------|
| **Hybrid REST+Worker pattern** | REST for queries/commands, broker consumers for synchronization, SignalR for real-time UI |
| **SignalR for real-time** | Built-in retry, fallback transports, cleaner than raw WebSocket |
| **Reliable broker (Redis Streams or RabbitMQ + MassTransit)** | Durable delivery, retries, and replay prevent data loss during service downtime |
| **Executor WebSocket as primary sync source** | Avoids Binance REST polling and reduces 429 rate-limit risk |
| **Saga-based session reset** | Prevents ghost orders from leaking into the next test session |
| **INSERT-only ledger** | Immutability enforced at DB level (triggers) + app level (idempotency) |
| **Separate React app** | Independent build/deploy cycle, cleaner separation of concerns |
| **Dual live mode support** | Feature must support both Mainnet and Testnet APIs, with no paper mode |
| **Decimal precision** | Prevents floating-point loss for financial calculations |
| **Idempotent APIs** | Safe to retry; deduplication via Binance tranId |

---

## Future Enhancements

1. **Caching & Performance**
   - Add Redis caching for frequently queried aggregations (daily P&L, session summaries)
   - Implement pagination for large ledger tables

2. **Reporting**
   - Monthly/quarterly P&L summaries
   - Attribution analysis (P&L by strategy, symbol, hour of day)
   - Tax reporting exports (CSV, JSON)

3. **Authentication & Authorization**
   - Integrate with Gateway JWT auth
   - Role-based access (multi-tenancy)

4. **Audit & Compliance**
   - Immutable audit trail in separate table for regulatory compliance
   - Digital signatures on critical transactions

5. **Advanced Reconciliation**
   - Three-way reconciliation: Ledger ↔ Binance ↔ Executor
   - Automated alerting for discrepancies

---

## Questions for Clarification

1. **Auth**: Does existing system use JWT? How to integrate with Gateway?
2. **Fee Categories**: Is enum (TRADING, FUNDING, LIQUIDATION, OTHER) sufficient?
3. **Edge Cases**: How to handle ROE when initial_balance = 0? (Return 0, null, or metric)
4. **Multi-Account**: Will system always have single virtual account, or support multiple?
5. **Timezone**: What timezone for timestamps? (UTC assumed, confirm?)

---

## Appendix: Quick Reference

### URLs
- Service Health: `http://localhost:5097/health`
- Account Balance: `GET http://localhost:5097/api/ledger/account/{accountId}`
- Ledger Entries: `GET http://localhost:5097/api/ledger/entries?sessionId={id}&page=1&pageSize=50`
- Reset Session: `POST http://localhost:5097/api/ledger/sessions/reset`
- SignalR Hub: `ws://localhost:5097/ledger-hub`
- Frontend: `http://localhost:5098`

### Key Tables
- `virtual_accounts` — Account registry (MAINNET/TESTNET)
- `test_sessions` — Session lifecycle (ACTIVE/ARCHIVED)
- `ledger_entries` — Immutable cash flow ledger (INSERT-only)

### Key Services
- `LedgerRepository` — All ledger CRUD
- `SessionManagementService` — Create/reset/archive sessions
- `PnlCalculationService` — P&L math + caching
- `TradeEventConsumerWorker` — Durable consumption of Executor WebSocket-derived events
- `SessionResetSagaService` — Distributed reset orchestration (halt/close/confirm)

### Durable Event Channels
- `ledger:events` — Durable trade/account event stream or queue
- `session-reset-saga` — Saga orchestration events
- `trading-engine:commands` — Halt/close commands and confirmations
