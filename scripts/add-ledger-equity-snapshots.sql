-- Migration: add-ledger-equity-snapshots.sql
-- Purpose: store accurate equity snapshots captured after each SELL (REALIZED_PNL)
-- Date: 2026-04-06

BEGIN;

CREATE TABLE IF NOT EXISTS public.ledger_equity_snapshots (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES public.test_sessions(id) ON DELETE CASCADE,
    trigger_transaction_id VARCHAR(255) NOT NULL,
    trigger_symbol VARCHAR(20),
    snapshot_time TIMESTAMPTZ NOT NULL,
    current_balance NUMERIC(20, 8) NOT NULL,
    holdings_market_value NUMERIC(20, 8) NOT NULL,
    total_equity NUMERIC(20, 8) NOT NULL,
    holdings_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ledger_equity_snapshots_session_trigger
    ON public.ledger_equity_snapshots (session_id, trigger_transaction_id);

CREATE INDEX IF NOT EXISTS idx_ledger_equity_snapshots_session_time
    ON public.ledger_equity_snapshots (session_id, snapshot_time DESC);

CREATE INDEX IF NOT EXISTS idx_ledger_equity_snapshots_trigger_symbol
    ON public.ledger_equity_snapshots (trigger_symbol);

COMMIT;
