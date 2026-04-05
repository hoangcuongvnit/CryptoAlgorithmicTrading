-- Migration: remove-paper-mode.sql
-- Purpose: remove legacy paper trading mode data and schema artifacts.
-- Policy: hard delete paper-mode data (no archive).

BEGIN;

-- 1) Hard-delete paper-mode rows in report/snapshot tables.
DELETE FROM public.session_reports
WHERE trading_mode = 'paper';

DELETE FROM public.session_capital_snapshot
WHERE trading_mode = 'paper';

-- 2) Remove paper marker column and paper rows from orders if column exists.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'orders'
          AND column_name = 'is_paper'
    ) THEN
        EXECUTE 'DELETE FROM public.orders WHERE is_paper = true';
        EXECUTE 'ALTER TABLE public.orders DROP COLUMN is_paper';
    END IF;
END $$;

-- Keep archive schema compatible if it still has legacy paper column.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'archive'
          AND table_name = 'orders_history'
          AND column_name = 'is_paper'
    ) THEN
        EXECUTE 'ALTER TABLE archive.orders_history DROP COLUMN is_paper';
    END IF;
END $$;

-- 3) Move budget tables to neutral names (paper_trading_* -> trading_*).
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'paper_trading_account')
       AND NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'trading_account') THEN
        EXECUTE 'ALTER TABLE public.paper_trading_account RENAME TO trading_account';
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'paper_trading_ledger')
       AND NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'trading_ledger') THEN
        EXECUTE 'ALTER TABLE public.paper_trading_ledger RENAME TO trading_ledger';
    END IF;
END $$;

-- 4) Tighten mode constraints to the two remaining modes.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'session_reports') THEN
        EXECUTE 'ALTER TABLE public.session_reports DROP CONSTRAINT IF EXISTS session_reports_trading_mode_check';
        EXECUTE 'ALTER TABLE public.session_reports ADD CONSTRAINT session_reports_trading_mode_check CHECK (trading_mode IN (''live'',''testnet''))';
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'session_capital_snapshot') THEN
        EXECUTE 'ALTER TABLE public.session_capital_snapshot DROP CONSTRAINT IF EXISTS session_capital_snapshot_trading_mode_check';
        EXECUTE 'ALTER TABLE public.session_capital_snapshot ALTER COLUMN trading_mode SET DEFAULT ''live''';
        EXECUTE 'ALTER TABLE public.session_capital_snapshot ADD CONSTRAINT session_capital_snapshot_trading_mode_check CHECK (trading_mode IN (''live'',''testnet''))';
    END IF;
END $$;

-- 5) Remove obsolete system settings keys tied to paper-mode toggle.
DELETE FROM public.system_settings
WHERE key IN (
    'trading.paperTradingMode',
    'trading.initialBalance',
    'trading.updatedBy',
    'trading.updatedAtUtc'
);

COMMIT;
