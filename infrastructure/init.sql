-- TimescaleDB initialization script for Crypto Trading System
-- This script creates hypertables for time-series data

-- Enable TimescaleDB extension
CREATE EXTENSION IF NOT EXISTS timescaledb;

-- =============================================
-- Price Ticks Table (Time-series data)
-- =============================================
CREATE TABLE IF NOT EXISTS price_ticks (
    time        TIMESTAMPTZ NOT NULL,
    symbol      TEXT        NOT NULL,
    price       NUMERIC     NOT NULL,
    volume      NUMERIC     NOT NULL,
    open        NUMERIC,
    high        NUMERIC,
    low         NUMERIC,
    close       NUMERIC,
    interval    TEXT
);

-- Enforce idempotent writes from realtime + historical pipelines
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'uq_price_ticks_time_symbol_interval'
    ) THEN
        ALTER TABLE price_ticks
            ADD CONSTRAINT uq_price_ticks_time_symbol_interval
            UNIQUE (time, symbol, interval);
    END IF;
END $$;

-- Convert to hypertable
SELECT create_hypertable('price_ticks', 'time', if_not_exists => TRUE);

-- Create indexes for common queries
CREATE INDEX IF NOT EXISTS idx_price_ticks_symbol_time ON price_ticks (symbol, time DESC);

-- =============================================
-- Data Gaps Tracking Table
-- =============================================
CREATE TABLE IF NOT EXISTS data_gaps (
    id          BIGSERIAL PRIMARY KEY,
    symbol      TEXT        NOT NULL,
    interval    TEXT        NOT NULL,
    gap_start   TIMESTAMPTZ NOT NULL,
    gap_end     TIMESTAMPTZ NOT NULL,
    detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    filled_at   TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_data_gaps_symbol_interval ON data_gaps (symbol, interval);
CREATE INDEX IF NOT EXISTS idx_data_gaps_unfilled ON data_gaps (filled_at) WHERE filled_at IS NULL;

-- =============================================
-- Trade Signals Table (Time-series data)
-- =============================================
CREATE TABLE IF NOT EXISTS trade_signals (
    time        TIMESTAMPTZ NOT NULL,
    symbol      TEXT        NOT NULL,
    rsi         NUMERIC,
    ema9        NUMERIC,
    ema21       NUMERIC,
    bb_upper    NUMERIC,
    bb_middle   NUMERIC,
    bb_lower    NUMERIC,
    strength    TEXT
);

-- Convert to hypertable
SELECT create_hypertable('trade_signals', 'time', if_not_exists => TRUE);

-- Create indexes
CREATE INDEX IF NOT EXISTS idx_trade_signals_symbol_time ON trade_signals (symbol, time DESC);

-- =============================================
-- Orders Table (Trade execution log)
-- =============================================
CREATE TABLE IF NOT EXISTS orders (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    time        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    symbol      TEXT        NOT NULL,
    side        TEXT        NOT NULL,
    order_type  TEXT        NOT NULL,
    quantity    NUMERIC     NOT NULL,
    price       NUMERIC,
    filled_price NUMERIC,
    filled_qty  NUMERIC,
    stop_loss   NUMERIC,
    take_profit NUMERIC,
    strategy    TEXT,
    is_paper    BOOLEAN     NOT NULL DEFAULT TRUE,
    success     BOOLEAN,
    error_msg   TEXT
);

-- Create indexes for orders
CREATE INDEX IF NOT EXISTS idx_orders_time ON orders (time DESC);
CREATE INDEX IF NOT EXISTS idx_orders_symbol ON orders (symbol);
CREATE INDEX IF NOT EXISTS idx_orders_is_paper ON orders (is_paper);

-- =============================================
-- Active Symbols Configuration Table
-- =============================================
CREATE TABLE IF NOT EXISTS active_symbols (
    symbol      TEXT PRIMARY KEY,
    enabled     BOOLEAN NOT NULL DEFAULT TRUE,
    added_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Insert some default symbols
INSERT INTO active_symbols (symbol, enabled) VALUES
    ('BTCUSDT', true),
    ('ETHUSDT', true),
    ('BNBUSDT', true),
    ('SOLUSDT', true),
    ('XRPUSDT', true)
ON CONFLICT (symbol) DO NOTHING;

-- =============================================
-- Account Balance Table (For risk management)
-- =============================================
CREATE TABLE IF NOT EXISTS account_balance (
    id          SERIAL PRIMARY KEY,
    time        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    balance     NUMERIC NOT NULL,
    currency    TEXT NOT NULL DEFAULT 'USDT'
);

-- Insert initial balance for testing (paper trading)
INSERT INTO account_balance (balance, currency) VALUES (10000.00, 'USDT');

-- =============================================
-- Continuous Aggregates (Optional - for performance)
-- =============================================

-- Daily OHLCV aggregate for price_ticks
CREATE MATERIALIZED VIEW IF NOT EXISTS price_ticks_daily
WITH (timescaledb.continuous) AS
SELECT 
    time_bucket('1 day', time) AS day,
    symbol,
    FIRST(open, time) as open,
    MAX(high) as high,
    MIN(low) as low,
    LAST(close, time) as close,
    SUM(volume) as volume
FROM price_ticks
GROUP BY day, symbol
WITH NO DATA;

-- Add refresh policy (refresh every hour)
SELECT add_continuous_aggregate_policy('price_ticks_daily',
    start_offset => INTERVAL '3 days',
    end_offset => INTERVAL '1 hour',
    schedule_interval => INTERVAL '1 hour',
    if_not_exists => TRUE);

-- =============================================
-- Retention Policies (Optional - to save space)
-- =============================================

-- Keep raw price ticks for 30 days
SELECT add_retention_policy('price_ticks', INTERVAL '30 days', if_not_exists => TRUE);

-- Keep trade signals for 90 days
SELECT add_retention_policy('trade_signals', INTERVAL '90 days', if_not_exists => TRUE);

-- =============================================
-- Useful Views
-- =============================================

-- View: Recent orders summary
CREATE OR REPLACE VIEW recent_orders_summary AS
SELECT 
    symbol,
    side,
    COUNT(*) as trade_count,
    SUM(CASE WHEN success THEN 1 ELSE 0 END) as successful_trades,
    AVG(CASE WHEN filled_price IS NOT NULL THEN filled_price ELSE 0 END) as avg_fill_price,
    MAX(time) as last_trade_time,
    is_paper
FROM orders
WHERE time > NOW() - INTERVAL '24 hours'
GROUP BY symbol, side, is_paper;

-- View: Daily PnL calculation (simplified - assumes equal position sizes)
CREATE OR REPLACE VIEW daily_pnl AS
SELECT 
    DATE(time) as trade_date,
    symbol,
    COUNT(*) as trades,
    SUM(CASE WHEN side = 'Buy' THEN -filled_price * filled_qty ELSE filled_price * filled_qty END) as pnl,
    is_paper
FROM orders
WHERE success = true AND filled_price IS NOT NULL AND filled_qty IS NOT NULL
GROUP BY trade_date, symbol, is_paper
ORDER BY trade_date DESC;

COMMENT ON TABLE price_ticks IS 'Time-series price tick data from Binance WebSocket';
COMMENT ON TABLE trade_signals IS 'Calculated technical indicator signals';
COMMENT ON TABLE orders IS 'Order execution log (both paper and live trades)';
COMMENT ON TABLE active_symbols IS 'Configuration for which symbols to track';
COMMENT ON TABLE account_balance IS 'Account balance snapshots for risk management';
