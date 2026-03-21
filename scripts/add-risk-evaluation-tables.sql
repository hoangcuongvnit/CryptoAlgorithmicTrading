-- ============================================================
-- Risk Evaluation Persistence Tables
-- Phase 1 of Safety & Risk Guard Transparency Plan
-- ============================================================

CREATE TABLE IF NOT EXISTS public.risk_evaluations (
    evaluation_id           UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    order_request_id        TEXT         NOT NULL,
    session_id              TEXT,
    symbol                  VARCHAR(20)  NOT NULL,
    side                    VARCHAR(10)  NOT NULL,
    requested_quantity      NUMERIC(20,8) NOT NULL,
    requested_price         NUMERIC(20,8),
    market_price_at_evaluation NUMERIC(20,8),
    outcome                 VARCHAR(20)  NOT NULL CHECK (outcome IN ('Safe', 'Risk', 'Rejected')),
    final_reason_code       TEXT,
    final_reason_message    TEXT,
    adjusted_quantity       NUMERIC(20,8),
    evaluated_at_utc        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    evaluation_latency_ms   BIGINT       NOT NULL DEFAULT 0,
    correlation_id          TEXT,
    raw_context_json        JSONB
);

CREATE TABLE IF NOT EXISTS public.risk_evaluation_rule_results (
    id               BIGSERIAL    PRIMARY KEY,
    evaluation_id    UUID         NOT NULL REFERENCES public.risk_evaluations(evaluation_id) ON DELETE CASCADE,
    rule_name        TEXT         NOT NULL,
    rule_version     TEXT         NOT NULL DEFAULT '1.0',
    result           VARCHAR(20)  NOT NULL CHECK (result IN ('Pass', 'Fail', 'Adjusted', 'Skipped')),
    reason_code      TEXT,
    reason_message   TEXT,
    threshold_value  TEXT,
    actual_value     TEXT,
    metadata_json    JSONB,
    duration_ms      BIGINT       NOT NULL DEFAULT 0,
    sequence_order   INT          NOT NULL DEFAULT 0
);

-- Indexes for common query patterns
CREATE INDEX IF NOT EXISTS idx_risk_evaluations_symbol_time
    ON public.risk_evaluations (symbol, evaluated_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_risk_evaluations_outcome_time
    ON public.risk_evaluations (outcome, evaluated_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_risk_evaluations_session_time
    ON public.risk_evaluations (session_id, evaluated_at_utc DESC)
    WHERE session_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_risk_evaluations_time
    ON public.risk_evaluations (evaluated_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_risk_rule_results_eval_rule
    ON public.risk_evaluation_rule_results (evaluation_id, rule_name);
