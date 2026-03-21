-- Migration: Fix column name mismatch + Add P&L tracking columns to orders table
-- Safe to run against a container initialized with init.sql (has error_message)
-- or against create-orders-table.sql (has error_msg).

-- Step 1: Rename error_message → error_msg if the old name exists
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'orders'
          AND column_name = 'error_message'
    ) THEN
        ALTER TABLE public.orders RENAME COLUMN error_message TO error_msg;
    END IF;
END $$;

-- Step 2: Add error_msg if it doesn't exist yet (covers edge cases)
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS error_msg TEXT;

-- Step 3: Add P&L tracking columns
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS status       VARCHAR(20) DEFAULT 'OPEN'
        CHECK (status IN ('OPEN', 'CLOSED', 'FAILED')),
    ADD COLUMN IF NOT EXISTS exit_price   DECIMAL(18, 8),
    ADD COLUMN IF NOT EXISTS exit_time    TIMESTAMP,
    ADD COLUMN IF NOT EXISTS realized_pnl DECIMAL(18, 8),
    ADD COLUMN IF NOT EXISTS roe_percent  DECIMAL(10, 4);

-- Step 4: Backfill status — handle nullable success column
UPDATE public.orders SET status = 'OPEN'   WHERE success = true  AND status IS NULL;
UPDATE public.orders SET status = 'FAILED' WHERE success = false AND status IS NULL;
UPDATE public.orders SET status = 'FAILED' WHERE success IS NULL AND status IS NULL;

-- Step 5: Make status NOT NULL now that all rows are backfilled
ALTER TABLE public.orders ALTER COLUMN status SET NOT NULL;
ALTER TABLE public.orders ALTER COLUMN status SET DEFAULT 'OPEN';

-- Index for open positions queries
CREATE INDEX IF NOT EXISTS idx_orders_status ON public.orders(status, time DESC);

-- Open positions view: most recent open order per symbol (long positions from BUY fills)
CREATE OR REPLACE VIEW public.open_positions AS
SELECT DISTINCT ON (symbol)
    id,
    symbol,
    side,
    filled_qty    AS quantity,
    filled_price  AS entry_price,
    time          AS opened_at
FROM public.orders
WHERE status = 'OPEN'
  AND success  = true
  AND side     = 'Buy'
  AND filled_qty IS NOT NULL
ORDER BY symbol, time DESC;

-- P&L summary view per symbol
CREATE OR REPLACE VIEW public.trading_pnl_summary AS
SELECT
    symbol,
    COUNT(*)                                          AS total_trades,
    COUNT(*) FILTER (WHERE realized_pnl > 0)         AS win_trades,
    COUNT(*) FILTER (WHERE realized_pnl < 0)         AS loss_trades,
    COALESCE(SUM(realized_pnl), 0)                   AS total_pnl,
    COALESCE(AVG(roe_percent), 0)                    AS avg_roe,
    MAX(realized_pnl)                                AS best_trade,
    MIN(realized_pnl)                                AS worst_trade
FROM public.orders
WHERE status = 'CLOSED'
  AND realized_pnl IS NOT NULL
GROUP BY symbol;
