-- Migration: add-equity-snapshot-event-type.sql
-- Purpose: tag each equity snapshot with the event that triggered it (SESSION_START, BUY, SELL)
-- Date: 2026-04-06

BEGIN;

ALTER TABLE public.ledger_equity_snapshots
    ADD COLUMN IF NOT EXISTS event_type VARCHAR(20) NOT NULL DEFAULT 'SELL';

CREATE INDEX IF NOT EXISTS idx_ledger_equity_snapshots_event_type
    ON public.ledger_equity_snapshots (session_id, event_type);

COMMIT;
