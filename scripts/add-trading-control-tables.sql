-- Migration: Add trading control operations table
-- Purpose: Persistent audit trail for close-all and shutdown operations

CREATE TABLE IF NOT EXISTS trading_control_operations (
    operation_id         UUID        PRIMARY KEY,
    operation_type       VARCHAR(32) NOT NULL,   -- 'close_all_now', 'close_all_scheduled'
    status               VARCHAR(32) NOT NULL,   -- Requested, Scheduled, Executing, Completed, CompletedWithErrors, Canceled
    requested_by         VARCHAR(100),
    reason               VARCHAR(200),
    requested_at_utc     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    scheduled_for_utc    TIMESTAMPTZ,
    started_at_utc       TIMESTAMPTZ,
    completed_at_utc     TIMESTAMPTZ,
    shutdown_ready       BOOLEAN NOT NULL DEFAULT FALSE,
    positions_closed_count INT,
    result_summary       JSONB,
    error_summary        JSONB,
    idempotency_key      VARCHAR(200) NOT NULL,
    correlation_id       VARCHAR(100),
    CONSTRAINT uq_trading_control_idempotency UNIQUE (idempotency_key)
);

CREATE INDEX IF NOT EXISTS idx_tco_status
    ON trading_control_operations(status);

CREATE INDEX IF NOT EXISTS idx_tco_requested_at
    ON trading_control_operations(requested_at_utc DESC);
