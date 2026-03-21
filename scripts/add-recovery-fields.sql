-- Migration: add crash-recovery audit fields to the orders table
-- Safe to run multiple times (all changes are guarded with IF NOT EXISTS / DO NOTHING patterns).

BEGIN;

-- Recovery run identifier — links all orders created during a single reconciliation run
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS recovery_run_id         UUID        NULL;

-- Source of the recovery event (startup_reconcile | manual_reconcile)
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS recovery_source         VARCHAR(64) NULL;

-- Outcome of reconciliation for this row (matched | corrected | forced_close)
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS reconciliation_status   VARCHAR(32) NULL;

-- Original session the order belonged to (useful when the order is a cross-session correction)
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS original_session_id     VARCHAR(32) NULL;

-- Whether this order was submitted as part of a recovery action
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS is_recovery_action      BOOLEAN     NOT NULL DEFAULT FALSE;

-- Recovery action type (cancel_stale_open | reduce_close | forced_flatten)
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS recovery_action_type    VARCHAR(32) NULL;

-- When the mismatch that triggered this action was first detected
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS recovery_detected_at_utc TIMESTAMPTZ NULL;

-- When the recovery action for this order was confirmed complete
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS recovery_completed_at_utc TIMESTAMPTZ NULL;

-- Exchange position quantity captured just before this recovery order was placed
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS exchange_position_qty_before NUMERIC(28, 10) NULL;

-- Exchange position quantity captured after this recovery order was confirmed
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS exchange_position_qty_after  NUMERIC(28, 10) NULL;

-- Index for fast look-up of all orders belonging to a recovery run
CREATE INDEX IF NOT EXISTS idx_orders_recovery_run_id
    ON public.orders (recovery_run_id)
    WHERE recovery_run_id IS NOT NULL;

-- Index for auditing all recovery actions
CREATE INDEX IF NOT EXISTS idx_orders_is_recovery_action
    ON public.orders (is_recovery_action, time DESC)
    WHERE is_recovery_action = TRUE;

COMMIT;
