-- Migration: add-ledger-schema.sql
-- Purpose: immutable financial ledger for FinancialLedger.Worker
-- Date: 2026-04-05

BEGIN;

CREATE TABLE IF NOT EXISTS public.virtual_accounts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    environment VARCHAR(20) NOT NULL CHECK (environment IN ('MAINNET', 'TESTNET')),
    base_currency VARCHAR(10) NOT NULL DEFAULT 'USDT',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (environment, base_currency)
);

CREATE TABLE IF NOT EXISTS public.test_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    account_id UUID NOT NULL REFERENCES public.virtual_accounts(id) ON DELETE CASCADE,
    algorithm_name VARCHAR(255) NOT NULL,
    initial_balance NUMERIC(20, 8) NOT NULL,
    start_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    end_time TIMESTAMPTZ,
    status VARCHAR(20) NOT NULL CHECK (status IN ('ACTIVE', 'ARCHIVED')),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_test_sessions_account_id
    ON public.test_sessions (account_id);

CREATE INDEX IF NOT EXISTS idx_test_sessions_status
    ON public.test_sessions (status);

CREATE TABLE IF NOT EXISTS public.ledger_entries (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES public.test_sessions(id) ON DELETE CASCADE,
    binance_transaction_id VARCHAR(255),
    type VARCHAR(50) NOT NULL CHECK (type IN (
        'INITIAL_FUNDING',
        'REALIZED_PNL',
        'COMMISSION',
        'FUNDING_FEE',
        'WITHDRAWAL'
    )),
    amount NUMERIC(20, 8) NOT NULL,
    symbol VARCHAR(20),
    timestamp TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ledger_entries_session_txn
    ON public.ledger_entries (session_id, binance_transaction_id)
    WHERE binance_transaction_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_ledger_entries_session_id
    ON public.ledger_entries (session_id);

CREATE INDEX IF NOT EXISTS idx_ledger_entries_session_timestamp
    ON public.ledger_entries (session_id, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_ledger_entries_symbol
    ON public.ledger_entries (symbol);

CREATE INDEX IF NOT EXISTS idx_ledger_entries_type
    ON public.ledger_entries (type);

CREATE OR REPLACE FUNCTION prevent_ledger_entry_updates()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'ledger_entries is immutable. Use INSERT only.';
END;
$$;

DROP TRIGGER IF EXISTS trg_ledger_entries_no_update ON public.ledger_entries;
CREATE TRIGGER trg_ledger_entries_no_update
    BEFORE UPDATE ON public.ledger_entries
    FOR EACH ROW
    EXECUTE FUNCTION prevent_ledger_entry_updates();

COMMIT;
