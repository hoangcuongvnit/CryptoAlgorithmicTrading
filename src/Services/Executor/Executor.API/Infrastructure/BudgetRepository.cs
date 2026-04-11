using Dapper;
using Npgsql;

namespace Executor.API.Infrastructure;

public sealed class BudgetRepository
{
    private readonly string _connectionString;
    private readonly ILogger<BudgetRepository> _logger;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _budgetSchemaEnsured;

    public BudgetRepository(IConfiguration configuration, ILogger<BudgetRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured");
        _logger = logger;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────

    public sealed record BudgetStatus(
        decimal InitialCapital,
        decimal CurrentCashBalance,
        decimal TotalRealizedPnL,
        decimal RoiPercent,
        string Currency,
        DateTime LastUpdatedUtc);

    public sealed record LedgerEntry(
        Guid Id,
        DateTime RecordedAtUtc,
        string ReferenceType,
        string? ReferenceId,
        decimal CashBalanceBefore,
        decimal CashBalanceAfter,
        decimal AdjustmentAmount,
        string? Description,
        string? CreatedBy);

    // ── Queries ───────────────────────────────────────────────────────────

    public async Task<BudgetStatus?> GetBudgetStatusAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT
                a.initial_capital,
                a.current_cash,
                a.currency,
                a.updated_at,
                COALESCE(SUM(sr.realized_pnl), 0) AS total_realized_pnl
            FROM public.trading_account a
            LEFT JOIN public.session_reports sr ON sr.trading_mode = 'live'
            WHERE a.is_active = TRUE
            GROUP BY a.initial_capital, a.current_cash, a.currency, a.updated_at
            LIMIT 1;
            """;
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await EnsureBudgetSchemaAsync(conn, ct);
            var row = await conn.QuerySingleOrDefaultAsync<dynamic>(
                new CommandDefinition(sql, cancellationToken: ct));
            if (row is null) return null;

            decimal initial = (decimal)row.initial_capital;
            decimal realized = (decimal)row.total_realized_pnl;
            decimal roi = initial > 0 ? decimal.Round(realized / initial * 100, 4) : 0m;

            return new BudgetStatus(
                InitialCapital: initial,
                CurrentCashBalance: (decimal)row.current_cash,
                TotalRealizedPnL: decimal.Round(realized, 4),
                RoiPercent: roi,
                Currency: (string)row.currency,
                LastUpdatedUtc: (DateTime)row.updated_at);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get budget status");
            return null;
        }
    }

    public async Task<(IReadOnlyList<LedgerEntry> transactions, int totalCount)> GetLedgerAsync(
        DateTime? from, DateTime? to, int limit, int offset, CancellationToken ct)
    {
        var conditions = new List<string>();
        if (from.HasValue) conditions.Add("recorded_at_utc >= @From");
        if (to.HasValue) conditions.Add("recorded_at_utc < @To");
        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        var sql = $"""
            SELECT id, recorded_at_utc, reference_type, reference_id,
                   cash_balance_before, cash_balance_after, adjustment_amount,
                   description, created_by
            FROM public.trading_ledger
            {where}
            ORDER BY recorded_at_utc DESC
            LIMIT @Limit OFFSET @Offset;
            """;
        var countSql = $"SELECT COUNT(*) FROM public.trading_ledger {where};";

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await EnsureBudgetSchemaAsync(conn, ct);
            var p = new { From = from, To = to, Limit = limit, Offset = offset };
            var rows = await conn.QueryAsync<dynamic>(new CommandDefinition(sql, p, cancellationToken: ct));
            var total = await conn.QuerySingleAsync<long>(new CommandDefinition(countSql, p, cancellationToken: ct));

            var entries = rows.Select(r => new LedgerEntry(
                Id: (Guid)r.id,
                RecordedAtUtc: (DateTime)r.recorded_at_utc,
                ReferenceType: (string)r.reference_type,
                ReferenceId: (string?)r.reference_id,
                CashBalanceBefore: (decimal)r.cash_balance_before,
                CashBalanceAfter: (decimal)r.cash_balance_after,
                AdjustmentAmount: (decimal)r.adjustment_amount,
                Description: (string?)r.description,
                CreatedBy: (string?)r.created_by
            )).ToList();

            return (entries, (int)total);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch ledger entries");
            return ([], 0);
        }
    }

    // ── Mutations ─────────────────────────────────────────────────────────

    public async Task<(bool success, string? error, Guid? transactionId, decimal newBalance)> DepositAsync(
        decimal amount, string? description, string? requestedBy, CancellationToken ct)
    {
        if (amount <= 0)
            return (false, "Amount must be positive", null, 0);
        if (amount > 1_000_000)
            return (false, "Amount exceeds maximum deposit limit of 1,000,000", null, 0);

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await EnsureBudgetSchemaAsync(conn, ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var before = await conn.QuerySingleOrDefaultAsync<decimal?>(
                new CommandDefinition(
                    "SELECT current_cash FROM public.trading_account WHERE is_active = TRUE LIMIT 1 FOR UPDATE;",
                    transaction: tx, cancellationToken: ct));

            if (!before.HasValue)
                return (false, "No active trading account found", null, 0);

            var after = before.Value + amount;
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE public.trading_account SET current_cash = @After, updated_at = NOW() WHERE is_active = TRUE;",
                new { After = after }, tx, cancellationToken: ct));

            var txId = Guid.NewGuid();
            await InsertLedgerEntryAsync(conn, tx, txId, "DEPOSIT", $"deposit_{txId:N}",
                before.Value, after, amount, description,
                string.IsNullOrWhiteSpace(requestedBy) ? "USER/unknown" : $"USER/{requestedBy}", ct);

            await tx.CommitAsync(ct);
            return (true, null, txId, after);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deposit {Amount}", amount);
            return (false, "Internal error during deposit", null, 0);
        }
    }

    public async Task<(bool success, string? error, Guid? transactionId, decimal newBalance)> WithdrawAsync(
        decimal amount, string? description, string? requestedBy, CancellationToken ct)
    {
        if (amount <= 0)
            return (false, "Amount must be positive", null, 0);

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await EnsureBudgetSchemaAsync(conn, ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var before = await conn.QuerySingleOrDefaultAsync<decimal?>(
                new CommandDefinition(
                    "SELECT current_cash FROM public.trading_account WHERE is_active = TRUE LIMIT 1 FOR UPDATE;",
                    transaction: tx, cancellationToken: ct));

            if (!before.HasValue)
                return (false, "No active trading account found", null, 0);

            if (amount > before.Value)
                return (false, $"Insufficient balance. Available: {before.Value:F2}, Requested: {amount:F2}", null, 0);

            var after = before.Value - amount;
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE public.trading_account SET current_cash = @After, updated_at = NOW() WHERE is_active = TRUE;",
                new { After = after }, tx, cancellationToken: ct));

            var txId = Guid.NewGuid();
            await InsertLedgerEntryAsync(conn, tx, txId, "WITHDRAW", $"withdraw_{txId:N}",
                before.Value, after, -amount, description,
                string.IsNullOrWhiteSpace(requestedBy) ? "USER/unknown" : $"USER/{requestedBy}", ct);

            await tx.CommitAsync(ct);
            return (true, null, txId, after);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to withdraw {Amount}", amount);
            return (false, "Internal error during withdrawal", null, 0);
        }
    }

    public async Task<(bool success, string? error, decimal newBalance, decimal previousBalance)> ResetAsync(
        decimal newCapital, string? description, string? requestedBy, CancellationToken ct)
    {
        if (newCapital <= 0)
            return (false, "New capital must be positive", 0, 0);

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await EnsureBudgetSchemaAsync(conn, ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var before = await conn.QuerySingleOrDefaultAsync<decimal?>(
                new CommandDefinition(
                    "SELECT current_cash FROM public.trading_account WHERE is_active = TRUE LIMIT 1 FOR UPDATE;",
                    transaction: tx, cancellationToken: ct));

            if (!before.HasValue)
                return (false, "No active trading account found", 0, 0);

            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE public.trading_account SET current_cash = @Cap, initial_capital = @Cap, updated_at = NOW() WHERE is_active = TRUE;",
                new { Cap = newCapital }, tx, cancellationToken: ct));

            var txId = Guid.NewGuid();
            await InsertLedgerEntryAsync(conn, tx, txId, "RESET", $"reset_{txId:N}",
                before.Value, newCapital, newCapital - before.Value,
                description ?? "Budget reset",
                string.IsNullOrWhiteSpace(requestedBy) ? "USER/unknown" : $"USER/{requestedBy}", ct);

            await tx.CommitAsync(ct);
            return (true, null, newCapital, before.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset budget to {NewCapital}", newCapital);
            return (false, "Internal error during reset", 0, 0);
        }
    }

    public sealed record EquityPoint(
        DateTime RecordedAtUtc,
        string ReferenceType,
        decimal CashBalance,
        decimal AdjustmentAmount);

    public sealed record CapitalFlowEvent(
        DateTime RecordedAtUtc,
        string EventType,           // INITIAL | SESSION_PNL | DEPOSIT | WITHDRAW | RESET | SNAPSHOT_OPEN | SNAPSHOT_CLOSE
        string? ReferenceId,
        decimal CashBalance,
        decimal? HoldingsValue,     // only for snapshots
        decimal? TotalEquity,       // only for snapshots
        decimal AdjustmentAmount,
        string? Description);

    // ── Equity Curve Query ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<EquityPoint>> GetEquityCurveAsync(
        DateTime? from, DateTime? to, CancellationToken ct)
    {
        var conditions = new List<string>();
        if (from.HasValue) conditions.Add("recorded_at_utc >= @From");
        if (to.HasValue) conditions.Add("recorded_at_utc < @To");
        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        var sql = $"""
            SELECT recorded_at_utc, reference_type, cash_balance_after AS cash_balance, adjustment_amount
            FROM public.trading_ledger
            {where}
            ORDER BY recorded_at_utc ASC;
            """;

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await EnsureBudgetSchemaAsync(conn, ct);
            var rows = await conn.QueryAsync<dynamic>(
                new CommandDefinition(sql, new { From = from, To = to }, cancellationToken: ct));

            return rows.Select(r => new EquityPoint(
                RecordedAtUtc: (DateTime)r.recorded_at_utc,
                ReferenceType: (string)r.reference_type,
                CashBalance: decimal.Round((decimal)r.cash_balance, 4),
                AdjustmentAmount: decimal.Round((decimal)r.adjustment_amount, 4)
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch equity curve");
            return [];
        }
    }

    // ── Capital Flow Timeline (ledger + snapshots merged) ─────────────────────

    public async Task<IReadOnlyList<CapitalFlowEvent>> GetCapitalFlowAsync(
        DateTime from, DateTime to, string mode, CancellationToken ct)
    {
        var startUtc = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(to.Date, DateTimeKind.Utc).AddDays(1);

        const string sql = """
            SELECT
                recorded_at_utc,
                reference_type   AS event_type,
                reference_id,
                cash_balance_after AS cash_balance,
                NULL::numeric    AS holdings_value,
                NULL::numeric    AS total_equity,
                adjustment_amount,
                description
            FROM public.trading_ledger
            WHERE recorded_at_utc >= @Start AND recorded_at_utc < @End

            UNION ALL

            SELECT
                recorded_at_utc,
                'SNAPSHOT_' || snapshot_type AS event_type,
                session_id       AS reference_id,
                cash_balance,
                holdings_value,
                total_equity,
                0::numeric       AS adjustment_amount,
                'Session ' || session_id || ' ' || snapshot_type AS description
            FROM public.session_capital_snapshot
            WHERE session_date >= @StartDate AND session_date < @EndDate
              AND trading_mode = @Mode

            ORDER BY recorded_at_utc ASC;
            """;

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await EnsureBudgetSchemaAsync(conn, ct);
            var rows = await conn.QueryAsync<dynamic>(new CommandDefinition(sql, new
            {
                Start = startUtc,
                End = endUtc,
                StartDate = startUtc.Date,
                EndDate = endUtc.Date,
                Mode = mode
            }, cancellationToken: ct));

            return rows.Select(r => new CapitalFlowEvent(
                RecordedAtUtc: (DateTime)r.recorded_at_utc,
                EventType: (string)r.event_type,
                ReferenceId: (string?)r.reference_id,
                CashBalance: decimal.Round((decimal)r.cash_balance, 4),
                HoldingsValue: r.holdings_value is null ? null : decimal.Round((decimal)r.holdings_value, 4),
                TotalEquity: r.total_equity is null ? null : decimal.Round((decimal)r.total_equity, 4),
                AdjustmentAmount: decimal.Round((decimal)r.adjustment_amount, 4),
                Description: (string?)r.description
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch capital flow from {From} to {To}", from.Date, to.Date);
            return [];
        }
    }

    // ── Session Snapshot Recording ────────────────────────────────────────────

    public async Task RecordSessionSnapshotAsync(
        string sessionId, int sessionNumber, string snapshotType,
        decimal cashBalance, decimal holdingsValue, int openPositionCount,
        string mode, CancellationToken ct)
    {
        // Parse session date from sessionId format "YYYYMMDD-SN"
        if (!DateOnly.TryParseExact(sessionId[..8], "yyyyMMdd", out var sessionDate))
        {
            _logger.LogWarning("Cannot parse session date from sessionId={SessionId}", sessionId);
            return;
        }

        const string sql = """
            INSERT INTO public.session_capital_snapshot
                (session_id, session_number, session_date, snapshot_type, trading_mode,
                 cash_balance, holdings_value, open_position_count, is_flat)
            VALUES
                (@SessionId, @SessionNumber, @SessionDate, @SnapshotType, @Mode,
                 @Cash, @Holdings, @OpenCount, @IsFlat)
            ON CONFLICT (session_id, snapshot_type, trading_mode) DO UPDATE SET
                cash_balance        = EXCLUDED.cash_balance,
                holdings_value      = EXCLUDED.holdings_value,
                open_position_count = EXCLUDED.open_position_count,
                is_flat             = EXCLUDED.is_flat,
                recorded_at_utc     = NOW();
            """;

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await EnsureBudgetSchemaAsync(conn, ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                SessionId = sessionId,
                SessionNumber = sessionNumber,
                SessionDate = sessionDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                SnapshotType = snapshotType,
                Mode = mode,
                Cash = cashBalance,
                Holdings = holdingsValue,
                OpenCount = openPositionCount,
                IsFlat = openPositionCount == 0
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record {Type} snapshot for session {SessionId}", snapshotType, sessionId);
        }
    }

    // ── Session PnL Recording (called automatically at session end) ──────────

    public async Task RecordSessionPnLAsync(string sessionId, decimal pnl, CancellationToken ct)
    {
        if (pnl == 0m) return; // nothing to record

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await EnsureBudgetSchemaAsync(conn, ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var before = await conn.QuerySingleOrDefaultAsync<decimal?>(
                new CommandDefinition(
                    "SELECT current_cash FROM public.trading_account WHERE is_active = TRUE LIMIT 1 FOR UPDATE;",
                    transaction: tx, cancellationToken: ct));

            if (!before.HasValue) { await tx.RollbackAsync(ct); return; }

            var after = before.Value + pnl;
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE public.trading_account SET current_cash = @After, updated_at = NOW() WHERE is_active = TRUE;",
                new { After = after }, tx, cancellationToken: ct));

            await InsertLedgerEntryAsync(conn, tx, Guid.NewGuid(), "SESSION_PNL", sessionId,
                before.Value, after, pnl,
                $"Session {sessionId} realized PnL", $"AUTOMATED/{sessionId}", ct);

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record session PnL for {SessionId}", sessionId);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Task InsertLedgerEntryAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid id, string refType, string refId,
        decimal before, decimal after, decimal adjustment,
        string? description, string? createdBy, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO public.trading_ledger
                (id, reference_type, reference_id, cash_balance_before, cash_balance_after,
                 adjustment_amount, description, created_by)
            VALUES
                (@Id, @RefType, @RefId, @Before, @After, @Adjustment, @Description, @CreatedBy);
            """;
        return conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            RefType = refType,
            RefId = refId,
            Before = before,
            After = after,
            Adjustment = adjustment,
            Description = description,
            CreatedBy = createdBy
        }, tx, cancellationToken: ct));
    }

    private async Task EnsureBudgetSchemaAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        if (_budgetSchemaEnsured)
        {
            return;
        }

        await _schemaLock.WaitAsync(ct);
        try
        {
            if (_budgetSchemaEnsured)
            {
                return;
            }

            const string bootstrapSql = """
                CREATE EXTENSION IF NOT EXISTS pgcrypto;

                CREATE TABLE IF NOT EXISTS public.trading_account (
                    id              UUID           PRIMARY KEY DEFAULT gen_random_uuid(),
                    current_cash    NUMERIC(20,8)  NOT NULL DEFAULT 10000.00,
                    initial_capital NUMERIC(20,8)  NOT NULL DEFAULT 10000.00,
                    currency        VARCHAR(5)     NOT NULL DEFAULT 'USDT',
                    is_active       BOOLEAN        NOT NULL DEFAULT TRUE,
                    created_at      TIMESTAMPTZ    NOT NULL DEFAULT NOW(),
                    updated_at      TIMESTAMPTZ    NOT NULL DEFAULT NOW()
                );

                INSERT INTO public.trading_account (current_cash, initial_capital, currency)
                SELECT 10000.00, 10000.00, 'USDT'
                WHERE NOT EXISTS (SELECT 1 FROM public.trading_account);

                CREATE INDEX IF NOT EXISTS idx_trading_account_active
                    ON public.trading_account (is_active) WHERE is_active = TRUE;

                CREATE TABLE IF NOT EXISTS public.trading_ledger (
                    id                  UUID           PRIMARY KEY,
                    recorded_at_utc     TIMESTAMPTZ    NOT NULL DEFAULT NOW(),
                    reference_type      VARCHAR(30)    NOT NULL,
                    reference_id        TEXT,
                    cash_balance_before NUMERIC(20,8)  NOT NULL,
                    cash_balance_after  NUMERIC(20,8)  NOT NULL,
                    adjustment_amount   NUMERIC(20,8)  NOT NULL,
                    description         TEXT,
                    created_by          VARCHAR(100),
                    currency            VARCHAR(5)     NOT NULL DEFAULT 'USDT'
                );

                CREATE INDEX IF NOT EXISTS idx_ledger_recorded_at
                    ON public.trading_ledger (recorded_at_utc DESC);
                CREATE INDEX IF NOT EXISTS idx_ledger_type
                    ON public.trading_ledger (reference_type);
                CREATE INDEX IF NOT EXISTS idx_ledger_reference
                    ON public.trading_ledger (reference_id);

                INSERT INTO public.trading_ledger
                    (id, reference_type, cash_balance_before, cash_balance_after, adjustment_amount, description, created_by)
                SELECT gen_random_uuid(), 'INITIAL', 0, a.initial_capital, a.initial_capital,
                       'System initialization - default trading budget', 'SYSTEM'
                FROM public.trading_account a
                WHERE a.is_active = TRUE
                  AND NOT EXISTS (SELECT 1 FROM public.trading_ledger WHERE reference_type = 'INITIAL')
                LIMIT 1;

                CREATE TABLE IF NOT EXISTS public.session_capital_snapshot (
                    id                  UUID           PRIMARY KEY DEFAULT gen_random_uuid(),
                    session_id          VARCHAR(20)    NOT NULL,
                    session_number      SMALLINT       NOT NULL,
                    session_date        DATE           NOT NULL,
                    snapshot_type       VARCHAR(10)    NOT NULL,
                    trading_mode        VARCHAR(10)    NOT NULL DEFAULT 'live',
                    cash_balance        NUMERIC(20,8)  NOT NULL,
                    holdings_value      NUMERIC(20,8)  NOT NULL DEFAULT 0,
                    total_equity        NUMERIC(20,8)  GENERATED ALWAYS AS (cash_balance + holdings_value) STORED,
                    open_position_count SMALLINT       NOT NULL DEFAULT 0,
                    is_flat             BOOLEAN        NOT NULL DEFAULT TRUE,
                    recorded_at_utc     TIMESTAMPTZ    NOT NULL DEFAULT NOW(),
                    UNIQUE (session_id, snapshot_type, trading_mode)
                );

                CREATE INDEX IF NOT EXISTS idx_session_snapshot_date
                    ON public.session_capital_snapshot (session_date DESC);
                CREATE INDEX IF NOT EXISTS idx_session_snapshot_session
                    ON public.session_capital_snapshot (session_id, trading_mode);
                """;

            await conn.ExecuteAsync(new CommandDefinition(bootstrapSql, cancellationToken: ct));
            _budgetSchemaEnsured = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }
}
