-- Migration: Add session tracking columns to orders table
-- Supports 4-hour session-locked trading campaign

ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS session_id          VARCHAR(20),
    ADD COLUMN IF NOT EXISTS session_phase        VARCHAR(30),
    ADD COLUMN IF NOT EXISTS is_reduce_only       BOOLEAN DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS forced_liquidation   BOOLEAN DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS liquidation_reason   VARCHAR(20);

-- Index for session-scoped queries
CREATE INDEX IF NOT EXISTS idx_orders_session_id
    ON public.orders(session_id, time DESC);

CREATE INDEX IF NOT EXISTS idx_orders_session_status
    ON public.orders(session_id, status);

-- Session compliance view
CREATE OR REPLACE VIEW public.session_compliance AS
SELECT
    session_id,
    COUNT(*)                                                AS total_orders,
    COUNT(*) FILTER (WHERE forced_liquidation = true)       AS forced_liquidations,
    COUNT(*) FILTER (WHERE status = 'OPEN')                 AS still_open,
    COALESCE(SUM(realized_pnl), 0)                          AS session_pnl,
    MIN(time)                                               AS first_order_time,
    MAX(COALESCE(exit_time, time))                          AS last_activity_time
FROM public.orders
WHERE session_id IS NOT NULL
GROUP BY session_id
ORDER BY session_id DESC;
