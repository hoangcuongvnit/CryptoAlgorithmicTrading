-- Migration: Add persistent session report tables
-- Spec: docs/report_buy_sell_dayly_4hour.md, Section 6
-- These tables store permanent, immutable session-level reporting data.
-- Live-computed versions come from the orders table via API endpoints.

CREATE TABLE IF NOT EXISTS public.session_reports (
    id                      UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    report_date             DATE          NOT NULL,
    session_id              TEXT          NOT NULL,
    trading_mode            TEXT          NOT NULL CHECK (trading_mode IN ('live', 'testnet')),
    session_start_utc       TIMESTAMPTZ   NOT NULL,
    session_end_utc         TIMESTAMPTZ   NOT NULL,
    opening_cash_balance    NUMERIC(20,8),
    opening_holdings_value  NUMERIC(20,8),
    opening_total_equity    NUMERIC(20,8),
    closing_cash_balance    NUMERIC(20,8),
    closing_holdings_value  NUMERIC(20,8),
    closing_total_equity    NUMERIC(20,8),
    realized_pnl            NUMERIC(20,8) NOT NULL DEFAULT 0,
    unrealized_pnl_at_close NUMERIC(20,8) NOT NULL DEFAULT 0,
    fees_total              NUMERIC(20,8) NOT NULL DEFAULT 0,
    other_costs_total       NUMERIC(20,8) NOT NULL DEFAULT 0,
    net_pnl                 NUMERIC(20,8) NOT NULL DEFAULT 0,
    equity_change_amount    NUMERIC(20,8),
    equity_change_percent   NUMERIC(10,6),
    trade_count_total       INT           NOT NULL DEFAULT 0,
    buy_count               INT           NOT NULL DEFAULT 0,
    sell_count              INT           NOT NULL DEFAULT 0,
    rejected_count          INT           NOT NULL DEFAULT 0,
    cancelled_count         INT           NOT NULL DEFAULT 0,
    distinct_symbols_count  INT           NOT NULL DEFAULT 0,
    symbols_csv             TEXT          NOT NULL DEFAULT '',
    is_flat_at_close        BOOLEAN       NOT NULL DEFAULT false,
    calculation_version     INT           NOT NULL DEFAULT 1,
    created_at              TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_session_report UNIQUE (report_date, session_id, trading_mode, calculation_version)
);

CREATE TABLE IF NOT EXISTS public.session_symbol_reports (
    id                UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    session_report_id UUID          NOT NULL REFERENCES public.session_reports(id) ON DELETE RESTRICT,
    symbol            TEXT          NOT NULL,
    buy_count         INT           NOT NULL DEFAULT 0,
    sell_count        INT           NOT NULL DEFAULT 0,
    buy_qty           NUMERIC(20,8) NOT NULL DEFAULT 0,
    sell_qty          NUMERIC(20,8) NOT NULL DEFAULT 0,
    avg_buy_price     NUMERIC(20,8),
    avg_sell_price    NUMERIC(20,8),
    realized_pnl      NUMERIC(20,8) NOT NULL DEFAULT 0,
    fees              NUMERIC(20,8) NOT NULL DEFAULT 0,
    net_pnl           NUMERIC(20,8) NOT NULL DEFAULT 0,
    win_trades        INT           NOT NULL DEFAULT 0,
    loss_trades       INT           NOT NULL DEFAULT 0,
    created_at        TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.session_equity_timeseries (
    id                UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    session_report_id UUID          NOT NULL REFERENCES public.session_reports(id) ON DELETE RESTRICT,
    point_time_utc    TIMESTAMPTZ   NOT NULL,
    cash_balance      NUMERIC(20,8),
    holdings_value    NUMERIC(20,8),
    total_equity      NUMERIC(20,8),
    created_at        TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

-- Indexes per spec section 6C
CREATE INDEX IF NOT EXISTS idx_session_reports_date_mode
    ON public.session_reports (report_date, trading_mode);

CREATE INDEX IF NOT EXISTS idx_session_reports_session_mode
    ON public.session_reports (session_id, trading_mode);

CREATE INDEX IF NOT EXISTS idx_session_symbol_reports_session_symbol
    ON public.session_symbol_reports (session_report_id, symbol);

CREATE INDEX IF NOT EXISTS idx_session_equity_ts_session_time
    ON public.session_equity_timeseries (session_report_id, point_time_utc);
