-- Create orders table for Executor.API
CREATE TABLE IF NOT EXISTS orders (
    id UUID PRIMARY KEY,
    time TIMESTAMP NOT NULL,
    symbol VARCHAR(20) NOT NULL,
    side VARCHAR(10) NOT NULL,
    order_type VARCHAR(20) NOT NULL,
    quantity DECIMAL(18, 8) NOT NULL,
    price DECIMAL(18, 8),
    filled_price DECIMAL(18, 8),
    filled_qty DECIMAL(18, 8),
    stop_loss DECIMAL(18, 8),
    take_profit DECIMAL(18, 8),
    strategy VARCHAR(100),
    success BOOLEAN NOT NULL,
    error_msg TEXT
);

CREATE INDEX IF NOT EXISTS idx_orders_time ON orders(time DESC);
CREATE INDEX IF NOT EXISTS idx_orders_symbol_time ON orders(symbol, time DESC);
