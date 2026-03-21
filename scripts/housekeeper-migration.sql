-- =============================================================================
-- HouseKeeper Migration: archive schema + audit log
-- Run once against the target database before enabling HouseKeeper.Worker.
-- =============================================================================

-- Archive schema for long-term order retention
CREATE SCHEMA IF NOT EXISTS archive;

-- Mirror of public.orders for archived rows
CREATE TABLE IF NOT EXISTS archive.orders_history (
    id           UUID        PRIMARY KEY,
    time         TIMESTAMPTZ NOT NULL,
    symbol       TEXT        NOT NULL,
    side         TEXT        NOT NULL,
    order_type   TEXT        NOT NULL DEFAULT 'Market',
    quantity     NUMERIC     NOT NULL,
    price        NUMERIC,
    filled_price NUMERIC,
    filled_qty   NUMERIC,
    stop_loss    NUMERIC,
    take_profit  NUMERIC,
    strategy     TEXT,
    is_paper     BOOLEAN     NOT NULL DEFAULT TRUE,
    success      BOOLEAN     NOT NULL DEFAULT FALSE,
    error_msg    TEXT,
    status       VARCHAR(20) NOT NULL,
    exit_price   NUMERIC,
    exit_time    TIMESTAMPTZ,
    realized_pnl NUMERIC,
    roe_percent  NUMERIC,
    archived_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_orders_history_time   ON archive.orders_history (time DESC);
CREATE INDEX IF NOT EXISTS idx_orders_history_symbol ON archive.orders_history (symbol);
CREATE INDEX IF NOT EXISTS idx_orders_history_status ON archive.orders_history (status, time DESC);

-- Audit log: one row per job per HouseKeeper run
CREATE TABLE IF NOT EXISTS public.housekeeper_audit_log (
    id           BIGSERIAL   PRIMARY KEY,
    run_at_utc   TIMESTAMPTZ NOT NULL,
    job_name     TEXT        NOT NULL,
    dry_run      BOOLEAN     NOT NULL,
    rows_affected BIGINT     NOT NULL DEFAULT 0,
    summary      TEXT,
    error        TEXT,
    duration_ms  BIGINT      NOT NULL DEFAULT 0,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_hk_audit_run_at ON public.housekeeper_audit_log (run_at_utc DESC);
CREATE INDEX IF NOT EXISTS idx_hk_audit_job    ON public.housekeeper_audit_log (job_name, run_at_utc DESC);
