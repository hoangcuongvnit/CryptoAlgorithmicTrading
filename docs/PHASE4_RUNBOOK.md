# Phase 4: Order Execution Engine - Runbook

## Overview
Phase 4 implements the complete order execution pipeline with risk validation and live order placement.

## Architecture

```
Redis Signal (e.g., signal:BTCUSDT) 
    ↓
Strategy.Worker (subscribes to signal:*)
    ↓
RiskGuard.API (gRPC validation)
    ↓
Executor.API (order execution + persistence)
    ↓
PostgreSQL (orders table) + Redis Streams (trades:audit)
```

## Service Dependencies & Ports

| Service | Port | Protocol | Dependencies |
|---------|------|----------|--------------|
| RiskGuard.API | 5013 | gRPC | PostgreSQL (for price lookups) |
| Executor.API | 5014 | gRPC | PostgreSQL (orders table), Redis (audit stream) |
| Strategy.Worker | N/A | Console | RiskGuard (5013), Executor (5014), Redis (6379) |

## Startup Sequence

1. **Start PostgreSQL** (if not already running)
   - Expected: Listening on port 5433
   - Connection: `Host=localhost;Port=5433;Database=cryptotrading;Username=postgres;Password=postgres`

2. **Create orders table** (one-time setup)
   ```bash
   cd scripts/CreateOrdersTable
   dotnet run
   ```

3. **Start RiskGuard.API**
   ```bash
   cd src/Services/RiskGuard/RiskGuard.API
   dotnet run --urls http://localhost:5013
   ```

4. **Start Executor.API**
   ```bash
   cd src/Services/Executor/Executor.API
   dotnet run --urls http://localhost:5014
   ```

5. **Start Strategy.Worker**
   ```bash
   cd src/Services/Strategy/Strategy.Worker
   dotnet run
   ```

## Configuration

### RiskGuard Risk Rules (appsettings.json)
```json
{
  "Risk": {
    "MinRiskReward": 2.0,           // R/R ratio must be >= 2.0
    "MaxOrderNotional": 1000,       // Max position size in USD
    "CooldownSeconds": 30,          // Minimum time between trades per symbol
    "AllowedSymbols": ["BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT"]
  }
}
```

### Executor Configuration (appsettings.json)
```json
{
  "Trading": {
    "PaperMode": true,             // Set to false for live trading
    "SlippageBasisPoints": 5,       // 0.05% slippage on paper fills
    "AllowedSymbols": ["BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT"]
  }
}
```

### Strategy Signal Mapping (appsettings.json)
```json
{
  "Trading": {
    "DefaultQuantity": 0.001,       // Default order size in base asset
    "StrongSignalThreshold": 2      // Enum: 0=Weak, 1=Medium, 2=Strong, 3=VeryStrong
  }
}
```

## Testing the Pipeline

### Test 1: Valid Signal → Accepted Order
```bash
# Publish a valid BTCUSDT signal
cd scripts/PublishSignal
dotnet run
```

Expected behavior:
- Strategy logs: "Order executed for BTCUSDT | Qty=0.001 | Price=<fill> | Paper=True"
- Database: New order row in `orders` table with `success=true`
- Redis Stream: New event in `trades:audit` with all order details

### Test 2: Disallowed Symbol → Rejected Order
Modify PublishSignal.Program.cs:
```csharp
symbol = "ADAUSDT"  // Not in RiskGuard AllowedSymbols
```

Expected behavior:
- Strategy logs: "Order rejected by RiskGuard for ADAUSDT: Symbol ADAUSDT is not allowed."
- **NO** order persisted to database

### Test 3: Cooldown Enforcement → Rejected Order
```bash
cd scripts/PublishSignal
dotnet run; Start-Sleep -Seconds 1; dotnet run  # Two signals 1 second apart
```

Expected behavior:
- First signal: "Order executed for BTCUSDT"
- Second signal: "Order rejected by RiskGuard for BTCUSDT: Cooldown active for BTCUSDT. Try again in Xs."
- Only **1** order persisted to database

## Database Schema

### orders table
```sql
CREATE TABLE orders (
    id UUID PRIMARY KEY,                    -- Unique order ID
    time TIMESTAMP NOT NULL,                -- Execution timestamp
    symbol VARCHAR(20) NOT NULL,            -- Trading pair (e.g., BTCUSDT)
    side VARCHAR(10) NOT NULL,              -- Buy or Sell
    order_type VARCHAR(20) NOT NULL,        -- Market or Limit
    quantity DECIMAL(18, 8) NOT NULL,       -- Order size in base asset
    price DECIMAL(18, 8),                   -- Limit order price (NULL for market)
    filled_price DECIMAL(18, 8),            -- Price at which order filled
    filled_qty DECIMAL(18, 8),              -- Actual quantity filled
    stop_loss DECIMAL(18, 8),               -- SL price
    take_profit DECIMAL(18, 8),             -- TP price
    strategy VARCHAR(100),                  -- Strategy name (e.g., "EMA_Crossover")
    is_paper BOOLEAN NOT NULL DEFAULT FALSE, -- TRUE = paper trade (legacy), FALSE = live
    success BOOLEAN NOT NULL,               -- Whether order was successfully executed
    error_msg TEXT                          -- Error details if success=FALSE
);

CREATE INDEX idx_orders_time ON orders(time DESC);
CREATE INDEX idx_orders_symbol_time ON orders(symbol, time DESC);
```

### Redis Streams: trades:audit
Each order execution publishes an event to the `trades:audit` stream:
```
XREVRANGE trades:audit + - COUNT 1

1772849648369-0
event_version           1
order_id                3686f4cd9642427aa973aae5eb7e6141
symbol                  BTCUSDT
side                    Buy
order_type              Market
filled_price            65013
filled_qty              0.001
is_paper                True
success                 True
error_code              
error_message           
time                    2026-03-07T02:14:08.3563590Z
```

## Verification Queries

### Check recent orders
```bash
cd scripts/QueryOrders
dotnet run
```

### Check audit trail
```bash
docker exec trading_redis redis-cli XREVRANGE trades:audit + - COUNT 5
```

### Test RiskGuard gRPC health
```bash
grpcurl -plaintext localhost:5013 grpc.health.v1.Health/Check
```

### Test Executor gRPC health
```bash
grpcurl -plaintext localhost:5014 grpc.health.v1.Health/Check
```

## Troubleshooting

### Strategy.Worker not receiving signals
- Check Redis is running: `docker ps | grep trading_redis`
- Verify RiskGuard and Executor are accessible from Strategy:
  - `netstat -an | findstr 5013` (RiskGuard)
  - `netstat -an | findstr 5014` (Executor)

### Orders not persisting
- Verify PostgreSQL is running: `Get-NetTCPConnection -LocalPort 5433`
- Verify orders table exists: `SELECT * FROM orders LIMIT 1;`
- Check Executor logs for persistence errors

### Audit stream empty
- Verify Redis is running with Streams support
- Check for errors in Executor logs about publishing to `trades:audit`

### Paper fills not realistic
- Adjust `SlippageBasisPoints` in Executor appsettings.json
- Paper simulator uses 8-decimal rounding for fill prices

## Known Limitations (Phase 4)

1. **Live trading only**: System executes orders on Binance API
2. **Slippage**: Fixed 5 BPS on all symbols; doesn't account for order book depth
3. **Risk rules**: Hardcoded per-symbol cooldown; no graduated position sizing
4. **Audit trail**: One-way write to Redis; no consumer processing audit events

## Performance Notes

- Strategy.Worker processes signals sequentially (one at a time)
- RiskGuard cooldown stored in-memory (ConcurrentDictionary); resets on service restart
- PostgreSQL orders table should be partitioned by time for datasets >1M rows
- Redis Streams retention: No auto-trimming (will grow indefinitely)

## Next Steps (Phase 5)

- Add real-time monitoring dashboard
- Implement order history API
- Add webhook notifications for fills/rejections
- Historical backtest engine
