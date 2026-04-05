# Paper Trading Removal - Complete Migration to Live + Testnet

> **Status**: ✅ **COMPLETED** — Paper trading removed entirely. Only **Live** and **Testnet** modes remain.  
> **Date**: 2026-03-15  
> **Breaking Change**: Yes — All paper trading references and simulators have been deleted.

---

## Overview

Paper trading mode has been completely removed from the system. The platform now operates in **two modes only**:

1. **Live** — Real Binance API (production trading)
2. **Testnet** — Binance Testnet API (safe order testing on Executor only)

| Mode | Before | After | Status |
|------|--------|-------|--------|
| Paper Trading | ✅ Enabled | ❌ **Removed** | **Deleted** |
| Live Trading | ✅ Enabled | ✅ Enabled | Unchanged |
| Testnet Trading | ✅ Available | ✅ Enhanced | Improved |

---

## What Was Deleted

### 1. Paper Mode Configuration

**Deleted from all appsettings.json**:
```json
// REMOVED - No longer exists
{
  "Trading": {
    "PaperTradingMode": true/false,
    "PaperTradeSlippage": 0.0005
  }
}
```

### 2. Code Files & Classes

| Component | File | Status |
|-----------|------|--------|
| Paper Order Simulator | `Infrastructure/PaperOrderSimulator.cs` | ❌ **Deleted** |
| Paper Trading Flag | `DTOs/OrderResult.cs` (IsPaperTrade property) | ❌ **Removed** |
| Paper Mode Checks | `OrderExecutionService.cs` (paper routing logic) | ❌ **Removed** |

### 3. Database Schema

**SQL Migration**: `scripts/remove-paper-mode.sql`

```sql
-- Remove paper trading schemas and tables
DELETE FROM public.orders WHERE is_paper = true;
ALTER TABLE public.orders DROP COLUMN is_paper;

-- Rename paper tables to live tables
ALTER TABLE paper_trading_account RENAME TO trading_account;
ALTER TABLE paper_trading_ledger RENAME TO trading_ledger;

-- Update constraints
ALTER TABLE public.orders 
ADD CONSTRAINT chk_trading_mode CHECK (trading_mode IN ('live', 'testnet'));
```

**Deleted Columns**:
- `orders.is_paper` (boolean flag)
- `budget_ledger.is_paper` (boolean flag)
- Any paper-specific audit fields

### 4. Frontend Components

**Deleted**:
- Paper mode toggle in Settings UI
- "Paper | Live" switch component
- Paper-specific report views (e.g., "Capital Flow: Live vs Paper")

**Updated**:
- TradingModePanel now shows only "Live ↔ Testnet" toggle
- Report pages no longer have "Paper" column

---

## Current Architecture: Live vs Testnet

### How It Works

```
API Key Management (Dual Configuration)
    ↓
┌───────────────────────────────────┐
│ BINANCE_USE_TESTNET = false       │
│ (environment variable)            │
└───────────────────────────────────┘
    ↓
    ├─ If false → Use Live API Keys
    │             (BINANCE_API_KEY, BINANCE_API_SECRET)
    │
    └─ If true  → Use Testnet API Keys
                  (BINANCE_TESTNET_API_KEY, BINANCE_TESTNET_API_SECRET)
```

### Binance API Scope

| Service | Binance Environment | Applies To |
|---------|--------------------|----|
| **Ingestor** | **Live** (always) | Price data ingestion |
| **Analyzer** | Uses Redis (no direct API calls) | Indicator calculation |
| **Strategy** | Uses Redis (no direct API calls) | Signal generation |
| **RiskGuard** | Uses Redis (no direct API calls) | Risk validation |
| **Executor** | **Live OR Testnet** (per BINANCE_USE_TESTNET) | Order execution |

**Key Point**: Testnet only affects **Executor order placement**. All other services continue to use **Live price data**.

---

## Configuration: Live vs Testnet

### Environment Variables (`.env`)

```env
# LIVE Configuration (Primary)
BINANCE_API_KEY=your_live_api_key
BINANCE_API_SECRET=your_live_api_secret

# TESTNET Configuration (for safe testing)
BINANCE_TESTNET_API_KEY=your_testnet_api_key
BINANCE_TESTNET_API_SECRET=your_testnet_api_secret

# MODE SELECTOR
BINANCE_USE_TESTNET=false  # false=Live, true=Testnet
```

### Code Reference

**File**: `src/Services/Executor/Executor.API/Configuration/Settings.cs`

```csharp
public class BinanceSettings
{
    public string ApiKey { get; set; }
    public string ApiSecret { get; set; }
    public string TestnetApiKey { get; set; }
    public string TestnetApiSecret { get; set; }
    public bool UseTestnet { get; set; } = false;

    // Automatically selects correct API keys based on UseTestnet
    public string ActiveApiKey => UseTestnet && !string.IsNullOrEmpty(TestnetApiKey)
        ? TestnetApiKey 
        : ApiKey;

    public string ActiveApiSecret => UseTestnet && !string.IsNullOrEmpty(TestnetApiSecret)
        ? TestnetApiSecret 
        : ApiSecret;
}
```

### Hot Swap (No Restart Required)

Use the Executor API endpoint to toggle modes at runtime:

```bash
# Switch to Testnet
POST /api/trading/reload-trading-config
Content-Type: application/json

{
  "binanceUseTestnet": true
}

# Response
{
  "status": "success",
  "mode": "testnet",
  "message": "Now using Testnet API credentials"
}
```

---

## Migration Path: Paper → Testnet

If you were using **Paper Trading** before, here's how to transition:

### Step 1: Enable Testnet Keys

Generate Testnet API keys from Binance:
```
https://testnet.binancefuture.com/
https://testnet.binance.vision/
```

### Step 2: Set Environment Variables

```env
BINANCE_TESTNET_API_KEY=your_testnet_key
BINANCE_TESTNET_API_SECRET=your_testnet_secret
BINANCE_USE_TESTNET=true  # Enable Testnet mode
```

### Step 3: Restart Services (or use hot-swap)

```bash
# Option A: Restart in Docker
docker compose restart executor

# Option B: Hot-swap at runtime
curl -X POST http://localhost:5094/api/trading/reload-trading-config \
  -H "Content-Type: application/json" \
  -d '{"binanceUseTestnet": true}'
```

### Step 4: Verify Mode

```bash
# Check current mode
curl http://localhost:5094/api/trading/session

# Response includes:
# "tradingMode": "testnet"
```

---

## Key Differences: Paper vs Testnet

| Aspect | Paper Mode (Old) | Testnet Mode (New) |
|--------|------------------|--------------------|
| **Order Execution** | Simulated fills with hardcoded slippage | Real Testnet exchange (live matching) |
| **Market Data** | Used Live Binance prices | Uses Live Binance prices (data source unchanged) |
| **API Calls** | No actual API calls | Real API calls to Testnet ✅ |
| **Fills** | Instant fills ±0.05% | Market-dependent fills (real slippage) |
| **Position Tracking** | In-memory simulation | Testnet orderbook (more realistic) |
| **Database** | Separate is_paper=true records | Regular orders with testnet credentials |
| **Use Case** | Quick backtesting | Safe pre-production testing |

---

## Benefits of Testnet vs Paper

✅ **More Realistic**: Uses actual exchange matching engine  
✅ **Slippage Accurate**: Real liquidity, real bid-ask spreads  
✅ **API Integration**: Tests Binance connectivity without risk  
✅ **Rate Limits**: Respects Testnet rate limit rules  
✅ **Zero Risk**: No real money at stake  
✅ **Production-Ready**: Same order types (Market/Limit/StopLimit) as Live  

---

## Database Migration

### Migration Script

If you have existing **paper trading orders**, run this:

```sql
-- Verify paper orders exist
SELECT COUNT(*) FROM orders WHERE is_paper = true;

-- BACKUP before deleting
CREATE TABLE archive.paper_orders_backup AS
SELECT * FROM orders WHERE is_paper = true;

-- Delete paper orders (optional)
DELETE FROM orders WHERE is_paper = true;

-- Drop unused paper column
ALTER TABLE orders DROP COLUMN is_paper;

-- New orders use trading_mode = 'live' or 'testnet'
-- (column already present in current schema)
```

### New Schema

```sql
-- orders table now uses trading_mode instead of is_paper
CREATE TABLE orders (
    id UUID PRIMARY KEY,
    symbol VARCHAR(20),
    side VARCHAR(10),          -- BUY, SELL
    quantity DECIMAL,
    entry_price DECIMAL,
    filled_price DECIMAL,
    status VARCHAR(20),        -- PENDING, FILLED, CANCELLED
    trading_mode VARCHAR(10),  -- 'live' OR 'testnet' (replaces is_paper)
    created_at TIMESTAMP,
    filled_at TIMESTAMP
);
```

---

## API Endpoint Changes

### Removed Endpoints

```
❌ GET /api/trading/report/capital-flow       # No longer exists
   (showed Paper vs Live comparison)

❌ PUT /api/trading/reload-trading-config     # Changed method
```

### Updated Endpoints

```
✅ POST /api/trading/reload-trading-config
   
   Request:
   {
     "binanceUseTestnet": true|false
   }
   
   Response:
   {
     "status": "success",
     "mode": "testnet|live"
   }
```

### New Endpoints (Testnet-Specific)

```
✅ POST /api/trading/validate-exchange
   Test Binance connectivity and credentials
   
   Response:
   {
     "isValid": true,
     "mode": "testnet",
     "balance": 1000.00,
     "message": "Testnet credentials valid"
   }
```

---

## Monitoring & Verification

### Check Current Mode

```bash
# Via API
curl http://localhost:5094/api/trading/session | grep tradingMode

# Via logs
docker compose logs executor | grep "Using.*API"
```

### Verify Orders

```bash
# Check order trading_mode
SELECT symbol, quantity, trading_mode, created_at 
FROM orders 
ORDER BY created_at DESC 
LIMIT 10;

# Expected output:
# symbol | quantity | trading_mode | created_at
# BTCUSDT | 0.001  | testnet     | 2026-03-15 10:30:45
```

---

## Troubleshooting

### Issue: Orders still marked as "paper"

**Solution**: 
1. Verify migration script ran: `SELECT COUNT(*) FROM orders WHERE is_paper IS NOT NULL;` should return 0
2. Check that column was dropped: `ALTER TABLE orders DROP COLUMN IF EXISTS is_paper;`
3. Restart Executor service

### Issue: Testnet API returns "Invalid API Key"

**Solution**:
1. Verify Testnet keys are different from Live keys: `echo $BINANCE_TESTNET_API_KEY` vs `echo $BINANCE_API_KEY`
2. Check Testnet credentials on https://testnet.binance.vision/
3. Regenerate Testnet keys and update .env
4. Restart Executor

### Issue: Frontend shows "Paper" mode option

**Solution**: Update `frontend/components/settings/TradingModePanel.jsx` to only show "Live ↔ Testnet" toggle (not "Paper | Live").

---

## Compliance Notes

✅ Paper mode removed and data cleaned  
✅ Testnet clearly labeled in UI/logs  
✅ No mixing of Live and Testnet trades in reports  
✅ Audit trail distinguishes between live/testnet orders  

---

## Reference Files

- **Code**: `src/Services/Executor/Executor.API/Services/OrderExecutionService.cs`
- **Configuration**: `src/Services/Executor/Executor.API/Configuration/Settings.cs`
- **Migration**: `scripts/remove-paper-mode.sql`
- **DTO**: `src/Shared/DTOs/OrderResult.cs` (check: IsPaperTrade removed)
- **API Docs**: `src/Services/Executor/Executor.API/Program.cs`
- **Frontend**: `frontend/components/settings/TradingModePanel.jsx`

---

**Next Steps**: Refer to [CLAUDE.md](CLAUDE.md) → Executor section for complete API documentation.
