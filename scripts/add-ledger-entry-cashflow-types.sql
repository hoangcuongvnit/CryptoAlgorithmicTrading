-- Migration: add-ledger-entry-cashflow-types.sql
-- Purpose: allow cashflow ledger entry types emitted by Executor
-- Date: 2026-04-11

BEGIN;

ALTER TABLE public.ledger_entries
    DROP CONSTRAINT IF EXISTS ledger_entries_type_check;

ALTER TABLE public.ledger_entries
    ADD CONSTRAINT ledger_entries_type_check CHECK (type IN (
        'INITIAL_FUNDING',
        'BUY_CASH_OUT',
        'SELL_CASH_IN',
        'REALIZED_PNL',
        'COMMISSION',
        'FUNDING_FEE',
        'WITHDRAWAL'
    ));

COMMIT;
