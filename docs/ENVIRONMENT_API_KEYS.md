# API Keys Management: Live & Testnet Configuration Guide

> **Current Model**: Dual API key architecture with hot-swap capability.  
> **Applies To**: Only **Executor** service for order placement. All other services use Live API for market data.  
> **Security**: API keys never logged; stored in encrypted system_settings table when configured via UI.

---

## Quick Start

```bash
# 1. Copy environment template
cp infrastructure/.env.template infrastructure/.env

# 2. Fill in your API keys
BINANCE_API_KEY=your_live_api_key
BINANCE_API_SECRET=your_live_api_secret
BINANCE_TESTNET_API_KEY=your_testnet_api_key
BINANCE_TESTNET_API_SECRET=your_testnet_api_secret

# 3. Set mode (false=Live, true=Testnet)
BINANCE_USE_TESTNET=false

# 4. Start services
docker compose up -d
```

---

## API Key Setup

### 1. Generate Live API Keys

**Binance Spot Trading**:
```
https://www.binance.com/en/user/settings/api-management
```

**Required Permissions**:
```
- Spot Trading
- Read
- Enable Withdrawal
- Account Holders must be able to perform User Acquired Transaction
```

**Result**:
```
API Key:    pxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
Secret Key: xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

### 2. Generate Testnet API Keys

**Binance Testnet (Spot)**:
```
https://testnet.binance.vision/
```

**Create Free Testnet Account** → Generates separate API credentials

**Result**:
```
API Key:    txxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx  (Testnet key)
Secret Key: xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx (Testnet secret)
```

---

## Configuration Methods

### Method 1: Environment Variables (Recommended for Deployment)

**File**: `infrastructure/.env`

```env
# ===== EXCHANGE CREDENTIALS =====
# Live Binance API (always required for Ingestor, Analyzer, Strategy)
BINANCE_API_KEY=your_live_api_key_here
BINANCE_API_SECRET=your_live_api_secret_here

# Testnet Binance API (optional, for Executor safe testing)
BINANCE_TESTNET_API_KEY=your_testnet_api_key_here
BINANCE_TESTNET_API_SECRET=your_testnet_api_secret_here

# Mode Selector (false=Live, true=Testnet - only affects Executor orders)
BINANCE_USE_TESTNET=false
```

**Applied to**:
```
docker compose up -d → All services read from .env
```

### Method 2: Hot Swap via API (Runtime - No Restart)

**Endpoint**: `POST /api/trading/reload-exchange-config`

**Use Case**: Switch between Live and Testnet without restarting Executor

```bash
curl -X POST http://localhost:5094/api/trading/reload-exchange-config \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
    "binanceApiKey": "new_live_key",
    "binanceApiSecret": "new_live_secret",
    "binanceTestnetApiKey": "new_testnet_key",
    "binanceTestnetApiSecret": "new_testnet_secret",
    "useTestnet": false
  }'
```

**Response**:
```json
{
  "status": "success",
  "message": "Exchange configuration reloaded",
  "mode": "live",
  "timestamp": "2026-03-15T10:30:45Z"
}
```

### Method 3: UI Settings Panel

**Frontend**: Settings → Exchange → Binance API

```
┌──────────────────────────────────┐
│ Binance API Configuration        │
├──────────────────────────────────┤
│ Live API Key:      [_________]   │
│ Live API Secret:   [_________]   │
│                                  │
│ Testnet API Key:   [_________]   │
│ Testnet API Secret:[_________]   │
│                                  │
│ Mode: [●Live ○Testnet]          │
│                                  │
│ [Test Connection] [Save]         │
└──────────────────────────────────┘
```

---

## Key Scope by Service

### Ingestor (Price Data Collection)

```
ALWAYS uses: BINANCE_API_KEY + BINANCE_API_SECRET

┌─────────────────────────────┐
│ Binance WebSocket           │
│ (Live Market Data)          │
└──────────┬──────────────────┘
           │
      Uses Live API
      1m klines for:
      - BTCUSDT, ETHUSDT,
      - BNBUSDT, SOLUSDT,
      - XRPUSDT
           │
    ┌──────▼────────┐
    │ Ingestor      │
    │ (WebSocket)   │
    │               │
    └──────┬────────┘
           │
    Publishes to Redis:
    - price:BTCUSDT
    - price:ETHUSDT
    - etc.
```

**Why Always Live?**
- Testnet WebSocket data is unreliable/unavailable
- Price analysis must use real market data

---

### Executor (Order Placement)

```
SWITCHES between: Live OR Testnet based on BINANCE_USE_TESTNET

┌─────────────────────┐
│ BINANCE_USE_TESTNET │
└──────────┬──────────┘
           │
    ┌──────┴─────────────┐
    │                    │
    ▼ (false)           ▼ (true)
┌─────────────┐    ┌──────────────┐
│ Live API    │    │ Testnet API  │
│             │    │              │
│ - POST      │    │ - POST       │
│   /order    │    │   /order     │
│ - DELETE    │    │ - DELETE     │
│   /order    │    │   /order     │
│ - GET       │    │ - GET        │
│   /account  │    │   /account   │
└──────┬──────┘    └──────┬───────┘
       │                   │
    Real Binance      Testnet Binance
   (Production)       (Safe Testing)
```

**Configuration**:
```env
BINANCE_USE_TESTNET=false  # Use Live (production)
BINANCE_USE_TESTNET=true   # Use Testnet (testing)
```

---

### Other Services (Analyzer, Strategy, RiskGuard)

```
NO BINANCE API CALLS

These services consume:
- Redis price data (from Ingestor)
- gRPC messages (from other services)
- PostgreSQL queries

Result: INDEPENDENT of API key configuration
```

---

## API Key Storage & Security

### At Startup

```
1. Read from environment variables (.env) or Docker secrets
2. Validate credentials with Binance API test call
3. Store encrypted copy in PostgreSQL (system_settings table)
   - Column: setting_value (encrypted with AES-256)
   - Column: setting_key = 'binance_api_live_key' etc.
4. Keep in-memory copy in BinanceSettings object
```

### Security Best Practices

✅ **Environment Variables**: Use Docker Secrets in production, not .env files  
✅ **Encryption**: API keys encrypted at rest in database  
✅ **Logging**: API keys NEVER logged (all endpoints mask in responses)  
✅ **Rotation**: Hot-swap endpoint allows key rotation without restart  
✅ **Scoping**: Executor hot-swap requires Bearer token authentication  

### Never Expose

```
❌ DON'T share .env file
❌ DON'T log API keys
❌ DON'T commit credentials to git
❌ DON'T use same key for multiple Executor instances (use Testnet for sharing)
```

---

## Testing Connectivity

### Test Live API

```bash
curl -X POST http://localhost:5094/api/trading/validate-exchange \
  -H "Content-Type: application/json" \
  -d '{"useTestnet": false}'

# Response:
{
  "isValid": true,
  "mode": "live",
  "balance": 1000.00,
  "message": "Live API credentials are valid"
}
```

### Test Testnet API

```bash
curl -X POST http://localhost:5094/api/trading/validate-exchange \
  -H "Content-Type: application/json" \
  -d '{"useTestnet": true}'

# Response:
{
  "isValid": true,
  "mode": "testnet",
  "balance": 10000.00,
  "message": "Testnet API credentials are valid"
}
```

---

## Workflow Examples

### Scenario 1: Start with Testnet (Safe Testing)

```bash
# 1. Set up .env
BINANCE_USE_TESTNET=true
BINANCE_TESTNET_API_KEY=testnet_key_xxx
BINANCE_TESTNET_API_SECRET=testnet_secret_xxx

# 2. Start services
docker compose up -d

# 3. Place test order (goes to Testnet Binance)
Strategy generates signal → Executor places order on Testnet

# 4. Monitor via API
curl http://localhost:5094/api/trading/positions
# Shows Testnet positions (no real risk)

# 5. Once confident, switch to Live
BINANCE_USE_TESTNET=false
docker compose up -d executor
```

### Scenario 2: Rotate Live API Keys (Hot Swap)

```bash
# 1. Generate new Live API key on Binance

# 2. Call hot-swap endpoint (no downtime!)
curl -X POST http://localhost:5094/api/trading/reload-exchange-config \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
    "binanceApiKey": "new_live_key_should_update_asap",
    "binanceApiSecret": "new_live_secret_here",
    "useTestnet": false
  }'

# 3. Executor continues trading with new credentials
# No order backlog, no session interruption

# 4. Delete old key on Binance for security
```

### Scenario 3: Multi-Environment Setup

```
┌─────────────────┐
│ Dev Environment │
└────────┬────────┘
         │
    .env (Testnet)
    BINANCE_USE_TESTNET=true
    BINANCE_TESTNET_API_KEY=test_key_dev
         │
    ┌────▼─────┐
    │ Executor  │
    │ (Dev)     │
    │ Testnet   │
    └───────────┘

┌─────────────────┐
│ Prod Environment│
└────────┬────────┘
         │
    .env (Live)
    BINANCE_USE_TESTNET=false
    BINANCE_API_KEY=live_key_prod
         │
    ┌────▼─────┐
    │ Executor  │
    │ (Prod)    │
    │ Live      │
    └───────────┘
```

---

## Troubleshooting

### Issue: "Invalid API Key" error

**Check**:
```bash
# 1. Verify .env is set correctly
grep BINANCE infrastructure/.env

# 2. Test connectivity
curl -X POST http://localhost:5094/api/trading/validate-exchange

# 3. Check if using wrong mode
# If BINANCE_USE_TESTNET=true but using Live keys → error

# 4. Verify API key permissions
#    Go to Binance → API Management → Check "Spot Trading" box
```

**Solution**:
```bash
# Update .env with correct key
BINANCE_API_KEY=correct_key_here
BINANCE_API_SECRET=correct_secret_here

# Restart Executor
docker compose restart executor
```

### Issue: "Rate limit exceeded"

**Likely Cause**: Using same API key on multiple instances

**Solution**:
```bash
# Option A: Use Testnet keys for non-production (load distribution)
BINANCE_TESTNET_API_KEY=key1
# instance 1: BINANCE_USE_TESTNET=true
# instance 2: BINANCE_USE_TESTNET=true

# Option B: Generate separate Testnet keys for each instance
# Each instance gets unique Testnet API pair
```

### Issue: Executor says "Testnet mode" but trading Live

**Check**:
```bash
# 1. Verify current mode
curl http://localhost:5094/api/trading/session | grep tradingMode

# 2. Check logs
docker compose logs executor | grep "Using.*API"

# 3. If logs show mismatch → restart
docker compose restart executor
```

---

## Environment Variables Reference

```env
# === REQUIRED ===
BINANCE_API_KEY=your_live_api_key_here
BINANCE_API_SECRET=your_live_api_secret_here

# === OPTIONAL (for Testnet testing) ===
BINANCE_TESTNET_API_KEY=your_testnet_api_key_here
BINANCE_TESTNET_API_SECRET=your_testnet_api_secret_here

# === MODE SELECTOR ===
BINANCE_USE_TESTNET=false
# false = Use Live API for Executor orders
# true  = Use Testnet API for Executor orders (safe testing)

# Note: Ingestor ALWAYS uses Live API (BINANCE_API_KEY/SECRET)
#       regardless of BINANCE_USE_TESTNET setting
```

---

## API Endpoints (Executor Port 5094)

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/trading/validate-exchange` | POST | Test API key validity |
| `/api/trading/reload-exchange-config` | POST | Hot-swap API keys (no restart) |
| `/api/trading/reload-trading-config` | POST | Toggle Live/Testnet mode |

---

## Reference

- **Configuration File**: `src/Services/Executor/Executor.API/Configuration/Settings.cs`
- **API Controller**: `src/Services/Executor/Executor.API/Program.cs` (see POST endpoints)
- **Binance REST Client**: `src/Services/Executor/Executor.API/Infrastructure/BinanceOrderClient.cs`
- **UI Component**: `frontend/components/settings/ExchangePanel.jsx`
- **Environment Template**: `infrastructure/.env.template`

---

**More Info**: See [CLAUDE.md](CLAUDE.md) → Executor section and [PAPER_TRADING_REMOVAL.md](PAPER_TRADING_REMOVAL.md).
