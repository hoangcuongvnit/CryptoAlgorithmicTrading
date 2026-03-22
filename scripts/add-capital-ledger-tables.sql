-- Migration: add-capital-ledger-tables.sql
-- Purpose: Add virtual budget and cash flow tracking for paper trading
-- Date: 2026-03-22
-- Spec: docs/Update_report_budget.md

BEGIN;

-- ── 1. Paper trading account: holds current cash state ───────────────────
CREATE TABLE IF NOT EXISTS public.paper_trading_account (
    id              UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    current_cash    NUMERIC(20,8) NOT NULL DEFAULT 10000.00,
    initial_capital NUMERIC(20,8) NOT NULL DEFAULT 10000.00,
    currency        VARCHAR(5)    NOT NULL DEFAULT 'USDT',
    is_active       BOOLEAN       NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

-- Seed with default paper trading budget (only if table is empty)
INSERT INTO public.paper_trading_account (current_cash, initial_capital, currency)
SELECT 10000.00, 10000.00, 'USDT'
WHERE NOT EXISTS (SELECT 1 FROM public.paper_trading_account);

CREATE INDEX IF NOT EXISTS idx_paper_trading_account_active
    ON public.paper_trading_account (is_active) WHERE is_active = TRUE;

-- ── 2. Audit ledger for all capital changes ──────────────────────────────
CREATE TABLE IF NOT EXISTS public.paper_trading_ledger (
    id                  UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    recorded_at_utc     TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    reference_type      VARCHAR(30)   NOT NULL
        CHECK (reference_type IN ('INITIAL', 'SESSION_PNL', 'DEPOSIT', 'WITHDRAW', 'RESET')),
    reference_id        TEXT,
    cash_balance_before NUMERIC(20,8) NOT NULL,
    cash_balance_after  NUMERIC(20,8) NOT NULL,
    adjustment_amount   NUMERIC(20,8) NOT NULL,  -- positive=in, negative=out
    description         TEXT,
    created_by          VARCHAR(100),
    currency            VARCHAR(5)    NOT NULL DEFAULT 'USDT'
);

CREATE INDEX IF NOT EXISTS idx_ledger_recorded_at
    ON public.paper_trading_ledger (recorded_at_utc DESC);
CREATE INDEX IF NOT EXISTS idx_ledger_type
    ON public.paper_trading_ledger (reference_type);
CREATE INDEX IF NOT EXISTS idx_ledger_reference
    ON public.paper_trading_ledger (reference_id);

-- ── 3. Seed initial ledger entry ─────────────────────────────────────────
INSERT INTO public.paper_trading_ledger
    (reference_type, cash_balance_before, cash_balance_after, adjustment_amount, description, created_by)
SELECT 'INITIAL', 0, a.initial_capital, a.initial_capital,
       'System initialization - default paper trading budget', 'SYSTEM'
FROM public.paper_trading_account a
WHERE a.is_active = TRUE
  AND NOT EXISTS (SELECT 1 FROM public.paper_trading_ledger WHERE reference_type = 'INITIAL')
LIMIT 1;

-- ── 4. Session capital snapshots ─────────────────────────────────────────
-- Records opening/closing cash + holdings value at each session boundary.
-- Enables equity decomposition (trading PnL vs capital operations).
CREATE TABLE IF NOT EXISTS public.session_capital_snapshot (
    id                  UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id          VARCHAR(20)   NOT NULL,          -- e.g. '20260322-S3'
    session_number      SMALLINT      NOT NULL,           -- 1-6
    session_date        DATE          NOT NULL,
    snapshot_type       VARCHAR(10)   NOT NULL            -- 'OPEN' or 'CLOSE'
        CHECK (snapshot_type IN ('OPEN', 'CLOSE')),
    trading_mode        VARCHAR(10)   NOT NULL DEFAULT 'paper'
        CHECK (trading_mode IN ('paper', 'live')),
    cash_balance        NUMERIC(20,8) NOT NULL,
    holdings_value      NUMERIC(20,8) NOT NULL DEFAULT 0, -- mark-to-market value of open positions
    total_equity        NUMERIC(20,8) GENERATED ALWAYS AS (cash_balance + holdings_value) STORED,
    open_position_count SMALLINT      NOT NULL DEFAULT 0,
    is_flat             BOOLEAN       NOT NULL DEFAULT TRUE, -- true when no open positions at snapshot time
    recorded_at_utc     TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    UNIQUE (session_id, snapshot_type, trading_mode)
);

CREATE INDEX IF NOT EXISTS idx_session_snapshot_date
    ON public.session_capital_snapshot (session_date DESC);
CREATE INDEX IF NOT EXISTS idx_session_snapshot_session
    ON public.session_capital_snapshot (session_id, trading_mode);

COMMIT;
