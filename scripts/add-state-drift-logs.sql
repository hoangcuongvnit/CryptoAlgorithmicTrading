-- Drift audit logs for periodic spot reconciliation
CREATE TABLE IF NOT EXISTS public.state_drift_logs (
    id UUID PRIMARY KEY,
    reconciliation_id UUID NOT NULL,
    reconciliation_utc TIMESTAMPTZ NOT NULL,
    symbol VARCHAR(20),
    drift_type VARCHAR(20) NOT NULL,
    environment VARCHAR(10) NOT NULL,
    binance_value NUMERIC(20,8) NOT NULL,
    local_value NUMERIC(20,8) NOT NULL,
    recovery_action VARCHAR(50) NOT NULL,
    recovery_detail VARCHAR(500) NOT NULL,
    severity VARCHAR(10) NOT NULL,
    recovery_attempted BOOLEAN NOT NULL DEFAULT FALSE,
    recovery_success BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_state_drift_logs_reconciliation_id
    ON public.state_drift_logs (reconciliation_id);

CREATE INDEX IF NOT EXISTS idx_state_drift_logs_symbol
    ON public.state_drift_logs (symbol)
    WHERE symbol IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_state_drift_logs_created_at
    ON public.state_drift_logs (created_at DESC);
