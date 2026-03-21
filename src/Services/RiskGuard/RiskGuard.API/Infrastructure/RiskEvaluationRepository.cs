using CryptoTrading.Shared.DTOs;
using Dapper;
using Npgsql;
using RiskGuard.API.Services;

namespace RiskGuard.API.Infrastructure;

/// <summary>
/// Persists and queries risk evaluation records in PostgreSQL.
/// Persistence is non-blocking — callers fire-and-forget.
/// Read failures never affect trading decisions.
/// </summary>
public interface IRiskEvaluationRepository
{
    Task SaveAsync(RiskEvaluationRecord evaluation, IReadOnlyList<RuleEvaluationDetail> ruleResults, CancellationToken ct = default);
    Task<(IReadOnlyList<RiskEvaluationDto> Items, int TotalCount)> GetPagedAsync(
        string? symbol, string? outcome, DateTime? from, DateTime? to, string? sessionId,
        int page, int pageSize, CancellationToken ct = default);
    Task<RiskEvaluationDto?> GetByIdAsync(Guid evaluationId, CancellationToken ct = default);
}

public sealed class RiskEvaluationRepository : IRiskEvaluationRepository
{
    private readonly string _connectionString;

    public RiskEvaluationRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Postgres connection string is required.");
    }

    public async Task SaveAsync(
        RiskEvaluationRecord evaluation,
        IReadOnlyList<RuleEvaluationDetail> ruleResults,
        CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        const string insertEval = """
            INSERT INTO public.risk_evaluations (
                evaluation_id, order_request_id, session_id, symbol, side,
                requested_quantity, requested_price, market_price_at_evaluation,
                outcome, final_reason_code, final_reason_message, adjusted_quantity,
                evaluated_at_utc, evaluation_latency_ms, correlation_id
            ) VALUES (
                @EvaluationId, @OrderRequestId, @SessionId, @Symbol, @Side,
                @RequestedQuantity, @RequestedPrice, @MarketPriceAtEvaluation,
                @Outcome, @FinalReasonCode, @FinalReasonMessage, @AdjustedQuantity,
                @EvaluatedAtUtc, @EvaluationLatencyMs, @CorrelationId
            )
            ON CONFLICT (evaluation_id) DO NOTHING
            """;

        await conn.ExecuteAsync(new CommandDefinition(insertEval, evaluation, transaction: tx, cancellationToken: ct));

        if (ruleResults.Count > 0)
        {
            const string insertRule = """
                INSERT INTO public.risk_evaluation_rule_results (
                    evaluation_id, rule_name, rule_version, result,
                    reason_code, reason_message, threshold_value, actual_value,
                    duration_ms, sequence_order
                ) VALUES (
                    @EvaluationId, @RuleName, @RuleVersion, @Result,
                    @ReasonCode, @ReasonMessage, @ThresholdValue, @ActualValue,
                    @DurationMs, @SequenceOrder
                )
                """;

            await conn.ExecuteAsync(new CommandDefinition(
                insertRule,
                ruleResults.Select(r => new
                {
                    evaluation.EvaluationId,
                    r.RuleName,
                    r.RuleVersion,
                    r.Result,
                    r.ReasonCode,
                    ReasonMessage = r.ReasonMessage,
                    r.ThresholdValue,
                    r.ActualValue,
                    r.DurationMs,
                    r.SequenceOrder
                }),
                transaction: tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    public async Task<(IReadOnlyList<RiskEvaluationDto> Items, int TotalCount)> GetPagedAsync(
        string? symbol, string? outcome, DateTime? from, DateTime? to, string? sessionId,
        int page, int pageSize, CancellationToken ct = default)
    {
        var conditions = new List<string>();
        var p = new DynamicParameters();

        if (!string.IsNullOrEmpty(symbol)) { conditions.Add("symbol = @symbol"); p.Add("symbol", symbol); }
        if (!string.IsNullOrEmpty(outcome)) { conditions.Add("outcome = @outcome"); p.Add("outcome", outcome); }
        if (from.HasValue) { conditions.Add("evaluated_at_utc >= @from"); p.Add("from", from.Value.ToUniversalTime()); }
        if (to.HasValue) { conditions.Add("evaluated_at_utc <= @to"); p.Add("to", to.Value.ToUniversalTime()); }
        if (!string.IsNullOrEmpty(sessionId)) { conditions.Add("session_id = @sessionId"); p.Add("sessionId", sessionId); }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        p.Add("pageSize", Math.Clamp(pageSize, 1, 100));
        p.Add("offset", (Math.Max(page, 1) - 1) * pageSize);

        var countSql = $"SELECT COUNT(*) FROM public.risk_evaluations {where}";
        var dataSql = $"""
            SELECT evaluation_id, order_request_id, session_id, symbol, side,
                   requested_quantity, requested_price, market_price_at_evaluation,
                   outcome, final_reason_code, final_reason_message, adjusted_quantity,
                   evaluated_at_utc, evaluation_latency_ms, correlation_id
            FROM public.risk_evaluations
            {where}
            ORDER BY evaluated_at_utc DESC
            LIMIT @pageSize OFFSET @offset
            """;

        await using var conn = new NpgsqlConnection(_connectionString);

        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, p, cancellationToken: ct));
        var rows = (await conn.QueryAsync<EvaluationRow>(new CommandDefinition(dataSql, p, cancellationToken: ct))).ToList();

        if (rows.Count == 0)
            return ([], total);

        var evalIds = rows.Select(r => r.EvaluationId).ToArray();
        const string rulesSql = """
            SELECT evaluation_id, rule_name, rule_version, result, reason_code, reason_message,
                   threshold_value, actual_value, duration_ms, sequence_order
            FROM public.risk_evaluation_rule_results
            WHERE evaluation_id = ANY(@ids)
            ORDER BY evaluation_id, sequence_order
            """;

        var allRules = (await conn.QueryAsync<RuleRow>(
            new CommandDefinition(rulesSql, new { ids = evalIds }, cancellationToken: ct))).ToList();

        var rulesByEval = allRules
            .GroupBy(r => r.EvaluationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = rows.Select(row => MapToDto(row, rulesByEval.GetValueOrDefault(row.EvaluationId) ?? [])).ToList();
        return (items, total);
    }

    public async Task<RiskEvaluationDto?> GetByIdAsync(Guid evaluationId, CancellationToken ct = default)
    {
        const string evalSql = """
            SELECT evaluation_id, order_request_id, session_id, symbol, side,
                   requested_quantity, requested_price, market_price_at_evaluation,
                   outcome, final_reason_code, final_reason_message, adjusted_quantity,
                   evaluated_at_utc, evaluation_latency_ms, correlation_id
            FROM public.risk_evaluations
            WHERE evaluation_id = @evaluationId
            """;

        const string rulesSql = """
            SELECT evaluation_id, rule_name, rule_version, result, reason_code, reason_message,
                   threshold_value, actual_value, duration_ms, sequence_order
            FROM public.risk_evaluation_rule_results
            WHERE evaluation_id = @evaluationId
            ORDER BY sequence_order
            """;

        await using var conn = new NpgsqlConnection(_connectionString);

        var row = await conn.QuerySingleOrDefaultAsync<EvaluationRow>(
            new CommandDefinition(evalSql, new { evaluationId }, cancellationToken: ct));
        if (row is null) return null;

        var rules = (await conn.QueryAsync<RuleRow>(
            new CommandDefinition(rulesSql, new { evaluationId }, cancellationToken: ct))).ToList();

        return MapToDto(row, rules);
    }

    // ── Private mapping helpers ───────────────────────────────────────────

    private static RiskEvaluationDto MapToDto(EvaluationRow row, IReadOnlyList<RuleRow> rules)
        => new()
        {
            EvaluationId = row.EvaluationId,
            OrderRequestId = row.OrderRequestId,
            SessionId = row.SessionId,
            Symbol = row.Symbol,
            Side = row.Side,
            RequestedQuantity = row.RequestedQuantity,
            RequestedPrice = row.RequestedPrice,
            MarketPriceAtEvaluation = row.MarketPriceAtEvaluation,
            Outcome = row.Outcome,
            FinalReasonCode = row.FinalReasonCode,
            FinalReasonMessage = row.FinalReasonMessage,
            AdjustedQuantity = row.AdjustedQuantity,
            EvaluatedAtUtc = row.EvaluatedAtUtc,
            EvaluationLatencyMs = row.EvaluationLatencyMs,
            CorrelationId = row.CorrelationId,
            RuleResults = rules.Select(r => new RiskRuleResultDto(
                r.RuleName, r.RuleVersion, r.Result,
                r.ReasonCode, r.ReasonMessage,
                r.ThresholdValue, r.ActualValue,
                r.DurationMs, r.SequenceOrder)).ToList()
        };

    // ── Private DB row types ──────────────────────────────────────────────

    private sealed class EvaluationRow
    {
        public Guid EvaluationId { get; init; }
        public string OrderRequestId { get; init; } = string.Empty;
        public string? SessionId { get; init; }
        public string Symbol { get; init; } = string.Empty;
        public string Side { get; init; } = string.Empty;
        public decimal RequestedQuantity { get; init; }
        public decimal? RequestedPrice { get; init; }
        public decimal? MarketPriceAtEvaluation { get; init; }
        public string Outcome { get; init; } = string.Empty;
        public string? FinalReasonCode { get; init; }
        public string? FinalReasonMessage { get; init; }
        public decimal? AdjustedQuantity { get; init; }
        public DateTime EvaluatedAtUtc { get; init; }
        public long EvaluationLatencyMs { get; init; }
        public string? CorrelationId { get; init; }
    }

    private sealed class RuleRow
    {
        public Guid EvaluationId { get; init; }
        public string RuleName { get; init; } = string.Empty;
        public string RuleVersion { get; init; } = string.Empty;
        public string Result { get; init; } = string.Empty;
        public string? ReasonCode { get; init; }
        public string? ReasonMessage { get; init; }
        public string? ThresholdValue { get; init; }
        public string? ActualValue { get; init; }
        public long DurationMs { get; init; }
        public int SequenceOrder { get; init; }
    }
}

/// <summary>Top-level evaluation record prepared by the gRPC service for persistence.</summary>
public sealed record RiskEvaluationRecord
{
    public required Guid EvaluationId { get; init; }
    public required string OrderRequestId { get; init; }
    public string? SessionId { get; init; }
    public required string Symbol { get; init; }
    public required string Side { get; init; }
    public required decimal RequestedQuantity { get; init; }
    public decimal? RequestedPrice { get; init; }
    public decimal? MarketPriceAtEvaluation { get; init; }
    public required string Outcome { get; init; }
    public string? FinalReasonCode { get; init; }
    public string? FinalReasonMessage { get; init; }
    public decimal? AdjustedQuantity { get; init; }
    public required DateTime EvaluatedAtUtc { get; init; }
    public required long EvaluationLatencyMs { get; init; }
    public string? CorrelationId { get; init; }
}
