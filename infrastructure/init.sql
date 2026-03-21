-- =============================================================================
-- CryptoAlgorithmicTrading — Full Database Initialization
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS timescaledb;

-- =============================================================================
-- Schemas
-- =============================================================================

CREATE SCHEMA IF NOT EXISTS historical_collector;

-- =============================================================================
-- historical_collector: yearly-partitioned price_ticks
-- =============================================================================

CREATE OR REPLACE FUNCTION historical_collector.create_price_ticks_table_for_year(year_val INT)
RETURNS void AS $$
DECLARE
    tbl  TEXT := 'price_' || year_val || '_ticks';
    s    DATE := make_date(year_val,     1, 1);
    e    DATE := make_date(year_val + 1, 1, 1);
BEGIN
    EXECUTE format('
        CREATE TABLE IF NOT EXISTS historical_collector.%I (
            time     TIMESTAMPTZ NOT NULL,
            symbol   TEXT        NOT NULL,
            price    NUMERIC     NOT NULL,
            volume   NUMERIC     NOT NULL,
            open     NUMERIC,
            high     NUMERIC,
            low      NUMERIC,
            close    NUMERIC,
            interval TEXT        NOT NULL,
            CONSTRAINT %I PRIMARY KEY (time, symbol, interval),
            CONSTRAINT %I CHECK (time >= %L::timestamptz AND time < %L::timestamptz)
        )', tbl, 'pk_'||tbl, 'chk_'||tbl||'_year', s, e);

    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON historical_collector.%I (symbol, time DESC);',
                   'idx_'||tbl||'_sym_time', tbl);
    EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON historical_collector.%I (symbol, interval, time DESC);',
                   'idx_'||tbl||'_sym_int_time', tbl);
END;
$$ LANGUAGE plpgsql;

SELECT historical_collector.create_price_ticks_table_for_year(2024);
SELECT historical_collector.create_price_ticks_table_for_year(2025);
SELECT historical_collector.create_price_ticks_table_for_year(2026);

-- Unified view across all yearly tables
CREATE OR REPLACE VIEW historical_collector.price_ticks AS
    SELECT time, symbol, price, volume, open, high, low, close, interval FROM historical_collector.price_2024_ticks
    UNION ALL
    SELECT time, symbol, price, volume, open, high, low, close, interval FROM historical_collector.price_2025_ticks
    UNION ALL
    SELECT time, symbol, price, volume, open, high, low, close, interval FROM historical_collector.price_2026_ticks;

-- Backward-compat view in public schema (keeps old code working)
CREATE OR REPLACE VIEW public.price_ticks AS
    SELECT * FROM historical_collector.price_ticks;

-- =============================================================================
-- historical_collector.data_gaps
-- =============================================================================

CREATE TABLE IF NOT EXISTS historical_collector.data_gaps (
    id          BIGSERIAL   PRIMARY KEY,
    symbol      TEXT        NOT NULL,
    interval    TEXT        NOT NULL,
    gap_start   TIMESTAMPTZ NOT NULL,
    gap_end     TIMESTAMPTZ NOT NULL,
    detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    filled_at   TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_data_gaps_symbol_interval ON historical_collector.data_gaps (symbol, interval);
CREATE INDEX IF NOT EXISTS idx_data_gaps_unfilled        ON historical_collector.data_gaps (filled_at) WHERE filled_at IS NULL;

-- =============================================================================
-- Helper: auto-create yearly table on first insert
-- =============================================================================

CREATE OR REPLACE FUNCTION historical_collector.ensure_price_ticks_table_exists(ts TIMESTAMPTZ)
RETURNS TEXT AS $$
DECLARE
    yr   INT  := EXTRACT(YEAR FROM ts);
    tbl  TEXT := 'price_' || yr || '_ticks';
    exists BOOLEAN;
BEGIN
    SELECT INTO exists
        (SELECT 1 FROM information_schema.tables
         WHERE table_schema = 'historical_collector' AND table_name = tbl) IS NOT NULL;

    IF NOT exists THEN
        PERFORM historical_collector.create_price_ticks_table_for_year(yr);

        -- Rebuild unified view to include new table
        EXECUTE (
            SELECT 'CREATE OR REPLACE VIEW historical_collector.price_ticks AS ' ||
                   string_agg(
                       'SELECT time,symbol,price,volume,open,high,low,close,interval FROM historical_collector.' || t.table_name,
                       ' UNION ALL '
                   )
            FROM information_schema.tables t
            WHERE t.table_schema = 'historical_collector'
              AND t.table_name LIKE 'price_%_ticks'
              AND t.table_type = 'BASE TABLE'
        );
    END IF;

    RETURN 'historical_collector.' || tbl;
END;
$$ LANGUAGE plpgsql;

-- =============================================================================
-- Orders table (public schema — used by Executor + RiskGuard)
-- =============================================================================

CREATE TABLE IF NOT EXISTS orders (
    id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    time         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    symbol       TEXT        NOT NULL,
    side         TEXT        NOT NULL,
    order_type   TEXT        NOT NULL DEFAULT 'Market',
    quantity     NUMERIC     NOT NULL,
    price        NUMERIC,
    filled_price NUMERIC,
    filled_qty   NUMERIC,
    stop_loss    NUMERIC,
    take_profit  NUMERIC,
    strategy     TEXT,
    is_paper     BOOLEAN     NOT NULL DEFAULT TRUE,
    success      BOOLEAN     NOT NULL DEFAULT FALSE,
    error_msg    TEXT,
    status       VARCHAR(20) NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN', 'CLOSED', 'FAILED')),
    exit_price   NUMERIC,
    exit_time    TIMESTAMPTZ,
    realized_pnl NUMERIC,
    roe_percent  NUMERIC
);

CREATE INDEX IF NOT EXISTS idx_orders_time     ON orders (time DESC);
CREATE INDEX IF NOT EXISTS idx_orders_symbol   ON orders (symbol);
CREATE INDEX IF NOT EXISTS idx_orders_is_paper ON orders (is_paper);
CREATE INDEX IF NOT EXISTS idx_orders_status   ON orders (status, time DESC);

CREATE OR REPLACE VIEW public.open_positions AS
SELECT DISTINCT ON (symbol)
    id, symbol, side,
    filled_qty   AS quantity,
    filled_price AS entry_price,
    time         AS opened_at
FROM public.orders
WHERE status = 'OPEN' AND success = true AND side = 'Buy' AND filled_qty IS NOT NULL
ORDER BY symbol, time DESC;

CREATE OR REPLACE VIEW public.trading_pnl_summary AS
SELECT symbol,
    COUNT(*)                                     AS total_trades,
    COUNT(*) FILTER (WHERE realized_pnl > 0)     AS win_trades,
    COUNT(*) FILTER (WHERE realized_pnl < 0)     AS loss_trades,
    COALESCE(SUM(realized_pnl), 0)               AS total_pnl,
    COALESCE(AVG(roe_percent), 0)                AS avg_roe,
    MAX(realized_pnl)                            AS best_trade,
    MIN(realized_pnl)                            AS worst_trade
FROM public.orders
WHERE status = 'CLOSED' AND realized_pnl IS NOT NULL
GROUP BY symbol;

-- =============================================================================
-- Active symbols configuration
-- =============================================================================

CREATE TABLE IF NOT EXISTS active_symbols (
    symbol   TEXT    PRIMARY KEY,
    enabled  BOOLEAN NOT NULL DEFAULT TRUE,
    added_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO active_symbols (symbol, enabled) VALUES
    ('BTCUSDT', true), ('ETHUSDT', true), ('BNBUSDT', true),
    ('SOLUSDT', true), ('XRPUSDT', true)
ON CONFLICT (symbol) DO NOTHING;

-- =============================================================================
-- Account balance (paper trading starting balance)
-- =============================================================================

CREATE TABLE IF NOT EXISTS account_balance (
    id       SERIAL  PRIMARY KEY,
    time     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    balance  NUMERIC NOT NULL,
    currency TEXT    NOT NULL DEFAULT 'USDT'
);

INSERT INTO account_balance (balance, currency) VALUES (10000.00, 'USDT');

-- =============================================================================
-- Trade signals
-- =============================================================================

CREATE TABLE IF NOT EXISTS trade_signals (
    time      TIMESTAMPTZ NOT NULL,
    symbol    TEXT        NOT NULL,
    rsi       NUMERIC,
    ema9      NUMERIC,
    ema21     NUMERIC,
    bb_upper  NUMERIC,
    bb_middle NUMERIC,
    bb_lower  NUMERIC,
    strength  TEXT
);

SELECT create_hypertable('trade_signals', 'time', if_not_exists => TRUE);
CREATE INDEX IF NOT EXISTS idx_trade_signals_symbol_time ON trade_signals (symbol, time DESC);
