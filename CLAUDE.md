# CLAUDE.md — AI Navigation Guide

> Quick-reference for AI agents. Optimized for token efficiency and fast context loading.

---

## QUICK COMMANDS

```bash
# Build & Test
dotnet build                                    # Build all
dotnet build src/Services/Executor/Executor.API/Executor.API.csproj
dotnet test
dotnet test --filter "ClassName=RsiCalculatorTests"
dotnet test /p:CollectCoverage=true

# Docker & Infrastructure (all services: databases + 12 services)
cd infrastructure && docker compose up -d redis postgres mongodb
cd infrastructure && docker compose up -d                          # Start all services
cd infrastructure && docker compose up -d redis postgres mongodb executor riskguard financialledger timelinelogger
cd infrastructure && docker compose down
cd infrastructure && docker compose build executor
cd infrastructure && docker compose logs -f strategy
cd infrastructure && docker compose logs -f financialledger
cd infrastructure && docker compose logs -f timelinelogger

# Observability (optional: Phase 5 stack)
cd infrastructure && docker compose -f docker-compose-observability.yml up -d
```

---

## PROJECT CONFIG

| Setting | Value |
|---------|-------|
| SDK | .NET 10.0 (`global.json` pins `10.0.103`) |
| Target | `net10.0` (all projects, `Directory.Build.props`) |
| Packages | Centrally managed in `Directory.Packages.props` — add `<PackageVersion>` there, no `Version=` in `.csproj` |
| Code style | File-scoped namespaces, 4-space indent, nullable enabled (`.editorconfig`) |

---

## LATEST UPDATES (2026-04-05)

### Effective Balance Migration (Env-Aware)

- `RiskGuard` no longer depends solely on static `VirtualAccountBalance` for core sizing/drawdown decisions.
- New provider: `IEffectiveBalanceProvider` + `EffectiveBalanceProvider`.
- Source routing by environment:
     - `TESTNET` → `FinancialLedger` (`/api/ledger/balance/effective`)
     - `MAINNET` → `Executor` reconciled snapshot (`/api/trading/balance/effective`)
- Compatibility switch retained: `Risk:AllowVirtualBalanceFallback` for safe rollout.
- `ValidateOrderRequest` now carries `environment` (proto + Strategy sender + RiskGuard receiver).

### Close-All Hardening (Executor)

- Added pre-close discovery and merge flow via `CloseAllDiscoveryService`:
     - Discover spot assets by notional threshold.
     - Map assets to canonical `BASEUSDT` close targets.
     - Merge discovered targets with locally tracked positions.
- Added post-close verification of leftovers and reasons (`NO_USDT_PAIR`, valuation unavailable, still present after close, etc.).
- Close-all status now exposes richer operation telemetry:
     - `discoveredCandidatesCount`, `attemptedCloseCount`, `verifiedAtUtc`, `leftovers`.

### Reconciliation + Runtime Safety (Executor)

- Added Binance↔local state synchronization in two phases:
     - Startup sync (`StartupReconciliationService`): rehydrate local positions and seed mainnet cash snapshot from Binance.
     - Periodic sync (`PeriodicReconciliationService`): detect drift between Binance Spot balances and local tracker, log drift rows, and auto-correct by policy.
- Added periodic reconciliation observability endpoints:
     - `GET /api/trading/reconciliation/health`
     - `GET /api/trading/reconciliation/latest`
- Added `ReconciliationMetrics` and cycle health snapshot.
- Added `CashBalanceStateService` for mainnet cash snapshot state.
- Added pre-check guards before order execution in both REST and gRPC paths:
     - Notional/amount validation (`OrderAmountLimitValidator`).
     - Spread and consensus validation (`SpreadFilterService`, `PriceConsensusService`).
     - Sell pre-check: local tracked quantity must be sufficient.
     - Buy pre-check: cash/budget must be sufficient (`BuyBudgetGuardService`).

### Container Networking Fix (Important)

- In Docker Compose, `RiskGuard` must call service DNS names, not `localhost`, for effective-balance lookup:
     - `Executor__BaseUrl=http://trading_executor:5094`
     - `FinancialLedger__BaseUrl=http://trading_financialledger:5097`

---

## ARCHITECTURE OVERVIEW

### Data Flow (End-to-End)

```
Binance WS → Ingestor → [TimescaleDB] + [Redis: price:SYMBOL]
                                                ↓
                                           Analyzer → [Redis: signal:SYMBOL]
                                                              ↓
                                                         Strategy → gRPC → RiskGuard
                                                                                ↓ (approved)
                                                                           Strategy → gRPC → Executor
                                                                                               ↓
                                                                          [TimescaleDB: orders] + [Redis Streams: trades:audit]
```

### Communication Protocols

| Protocol | Used For | Key Files |
|----------|----------|-----------|
| Redis Pub/Sub | Ingestor→Analyzer→Strategy | `src/Shared/Constants/RedisChannels.cs` |
| gRPC/Protobuf | Strategy→RiskGuard→Executor | `src/Shared/ProtoFiles/` |
| Redis Streams | Audit log (all trades) | Stream key: `trades:audit` |

> **gRPC note**: All gRPC services require `AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)` for unencrypted HTTP/2.

### Redis Channel Names

```
price:{SYMBOL}          # PriceTick (Ingestor → Analyzer)
signal:{SYMBOL}         # TradeSignal (Analyzer → Strategy)
trades:audit            # Redis Stream audit log (Executor publishes)
executor:trading:mode   # Current trading mode broadcast
coin:{SYMBOL}:log       # Timeline events (all services → TimelineLogger)
system:config:timezone  # Redis STRING key: active IANA timezone (e.g. "Asia/Ho_Chi_Minh")
                        #   Written by Gateway on startup and on timezone save
                        #   Read by Notifier on startup for Telegram message formatting
system:config:changed   # Redis Pub/Sub: broadcast when system config changes
                        #   Message payload = new IANA timezone string
                        #   Notifier subscribes → hot-reloads timezone without restart
ledger:events           # Redis Stream: trade events → FinancialLedger
                        #   Consumer group: financial-ledger
                        #   Fields: type, amount, accountId, environment, algorithmName,
                        #           timestamp, symbol, binanceTransactionId
trading-engine:commands # Redis Pub/Sub: control commands (e.g. HALT_AND_CLOSE_ALL)
                        #   Published by FinancialLedger SessionResetSagaService
```

### Service Types

| Type | Builder | Services |
|------|---------|----------|
| BackgroundService workers | `Host.CreateApplicationBuilder` | Ingestor, Analyzer, Strategy, Notifier, HistoricalCollector, HouseKeeper |
| gRPC API servers | `WebApplication.CreateBuilder` | RiskGuard (5013/5093), Executor (5014/5094) |
| Web API workers | `WebApplication.CreateBuilder` | TimelineLogger (5096), FinancialLedger (5097) |
| Web gateway | `WebApplication.CreateBuilder` | Gateway.API (5000) |

### Session Structure (Live Trading Only)

> **3×8-hour sessions per day** (changed from 6×4-hour). Paper trading has been completely removed.

| Session | Time (UTC) | Trading Hours |
|---------|-----------|-----------------|
| S1 | 00:00 → 08:00 | 7.5h (30min liquidation) |
| S2 | 08:00 → 16:00 | 7.5h (30min liquidation) |
| S3 | 16:00 → 24:00 | 7.5h (30min liquidation) |

### Binance API Modes (Live & Testnet)

> **Dual API configuration**: Live (primary) + Testnet (for safe order testing on Executor only)

| Service | Binance Environment | Lý do |
|---------|--------------------|----|
| **Executor** | Live hoặc **Testnet** (via `BINANCE_USE_TESTNET` env var) | Đặt/huỷ lệnh — Testnet để test an toàn |
| **Ingestor** | **Live** (luôn luôn) | Binance testnet không cung cấp WebSocket market data |
| **All Others** | **Live** (Analyzer, Strategy, RiskGuard, Notifier) | Không gọi Binance trực tiếp, chỉ dùng Redis/gRPC |

---

## SERVICE MAP

### Ingestor — `src/Services/DataIngestor/Ingestor.Worker/`

**Role**: Binance WebSocket → TimescaleDB + Redis

| File | Purpose |
|------|---------|
| `Program.cs` | Setup: Binance client, workers |
| `Workers/BinanceIngestorWorker.cs` | WebSocket 1m kline subscription |
| `Workers/PriceTickPersistenceWorker.cs` | Batch write to DB |
| `Workers/SymbolConfigListener.cs` | Redis-driven symbol changes |
| `Infrastructure/PriceTickRepository.cs` | PostgreSQL write |
| `Infrastructure/PriceTickWriteQueue.cs` | Async batching queue |
| `Infrastructure/RedisPublisher.cs` | Publishes to `price:SYMBOL` |
| `Configuration/Settings.cs` | AllowedSymbols, BatchSize, Binance |

**Default symbols**: `BTCUSDT, ETHUSDT, BNBUSDT, SOLUSDT, XRPUSDT`

---

### Analyzer — `src/Services/Analyzer/Analyzer.Worker/`

**Role**: Price ticks → Indicator calculation → TradeSignal

| File | Purpose |
|------|---------|
| `Program.cs` | Setup: IndicatorEngine DI |
| `Workers/SignalAnalyzerWorker.cs` | Subscribes to `price:SYMBOL`, triggers engine |
| `Workers/FundingRateFetcherWorker.cs` | Fetches perpetual funding rates |
| `Analysis/IndicatorEngine.cs` | **CORE LOGIC**: RSI(14), EMA(9/21), BB(20,2σ), ADX(14), ATR(14), volume, regime |
| `Analysis/PriceBuffer.cs` | Rolling 120-candle window per symbol |
| `Infrastructure/SignalPublisher.cs` | Publishes to `signal:SYMBOL` |
| `Infrastructure/FundingRateCache.cs` | Funding rate safety gate (max 2%) |
| `Configuration/AnalyzerSettings.cs` | All indicator parameters |

**Key thresholds** (appsettings): RSI oversold=35, overbought=65, BBSqueezeMultiplier=0.8, VolumeConfirmationRatio=1.5

---

### Strategy — `src/Services/Strategy/Strategy.Worker/`

**Role**: TradeSignal → OrderRequest → gRPC to RiskGuard

| File | Purpose |
|------|---------|
| `Program.cs` | Setup: gRPC clients (RiskGuard, Executor) |
| `Worker.cs` | Subscribes to `signal:*` pattern |
| `Services/SignalToOrderMapper.cs` | **CORE LOGIC**: Signal→Order with EMA cross, adaptive SL/TP |
| `Configuration/Settings.cs` | MinimumSignalStrength, DefaultOrderQuantity, AtrSlMultiplier |

**Mapping logic**:
- EMA9 >= EMA21 → BUY; else SELL
- Entry price: BB middle or EMA9
- Adaptive SL/TP: ATR × multiplier (fallback: 1.5%/3%)
- SoftUnwind phase: only Strong signals pass

---

### RiskGuard — `src/Services/RiskGuard/RiskGuard.API/`

**Role**: gRPC validation service, 9-rule sequential chain

**Ports**: gRPC=5013, HTTP=5093

| File | Purpose |
|------|---------|
| `Program.cs` | Rule registration order (critical!) |
| `Services/RiskGuardGrpcService.cs` | gRPC endpoint |
| `Services/RiskValidationEngine.cs` | Runs rule chain, short-circuits on reject |
| `Services/EffectiveBalanceProvider.cs` | **CORE LOGIC**: Environment-aware effective balance lookup with cache/fallback |
| `Rules/IRiskRule.cs` | Rule interface |
| `Infrastructure/OrderStatsRepository.cs` | Daily P&L queries |
| `Infrastructure/RiskEvaluationRepository.cs` | Audit trail (all evaluations) |
| `Infrastructure/RedisPersistenceService.cs` | Cooldowns in Redis |
| `Services/ValidationHistory.cs` | In-memory recent validation cache |
| `Configuration/RiskSettings.cs` | All rule thresholds |

**Rules** (evaluated in registration order):

| Rule File | Blocks When |
|-----------|------------|
| `Rules/ExitOnlyRule.cs` | Exit-only mode active |
| `Rules/RecoveryWindowRule.cs` | In recovery window after drawdown |
| `Rules/NoCrossSessionCarryRule.cs` | Position would cross session boundary |
| `Rules/SessionWindowRule.cs` | Outside allowed session hours |
| `Rules/SymbolAllowListRule.cs` | Symbol not in whitelist |
| `Rules/QuantityRule.cs` | Quantity out of min/max range |
| `Rules/PositionSizeRule.cs` | Position > max % of account |
| `Rules/RiskRewardRule.cs` | R:R ratio below minimum |
| `Rules/CooldownRule.cs` | Symbol in cooldown period |
| `Rules/MaxDrawdownRule.cs` | Daily loss limit exceeded |

**HTTP endpoints** (5093):
```
GET  /api/risk/config                   # Current thresholds
                                      # Includes effectiveBalance metadata (amount/source/env/stale/fallback)
POST /api/risk/reload-config            # Hot-swap settings
GET  /api/risk/stats                    # Today's validations, drawdown
                                      # Drawdown base now uses effective balance source
GET  /api/risk/persistence-status       # Redis cooldown state
GET  /api/risk-evaluations              # Paged evaluation history
GET  /api/risk-evaluations/{id}         # Single evaluation
GET  /health
```

---

### Executor — `src/Services/Executor/Executor.API/`

**Role**: Order execution, position management, reports, budget

**Ports**: gRPC=5014, HTTP=5094

| File | Purpose |
|------|---------|
| `Program.cs` | OpenTelemetry, gRPC, REST, Npgsql legacy timestamp |
| `Services/OrderExecutorGrpcService.cs` | gRPC PlaceOrder endpoint |
| `Services/OrderExecutionService.cs` | **CORE LOGIC**: Pre-check pipeline + execution routing to Live/Testnet BinanceOrderClient |
| `Services/LiquidationOrchestrator.cs` | Closes positions at session end (8-hour session boundary) |
| `Services/PartialTpMonitorService.cs` | Multi-stage take-profit management |
| `Services/StartupReconciliationService.cs` | Startup reconciliation: sync local tracker with Binance and seed cash snapshot |
| `Services/ShutdownOperationService.cs` | Tracks close-all, exit-only mode |
| `Services/CloseAllExecutorService.cs` | Parallel order cancellation |
| `Services/CloseAllDiscoveryService.cs` | **CORE LOGIC**: Discovery→merge→verify flow for complete close-all coverage |
| `Services/PeriodicReconciliationService.cs` | Periodic Binance↔local drift detection/recovery + drift logging |
| `Services/SessionExitOnlyMonitorService.cs` | Enforces exit-only in final 30 minutes of 8-hour session |
| `Services/RecoveryStateService.cs` | Tracks post-drawdown recovery window |
| `Services/BuyBudgetGuardService.cs` | Buy-side pre-check cash validation (testnet ledger / mainnet reconciled snapshot) |
| `Infrastructure/BinanceOrderClient.cs` | Binance REST API integration (live or testnet via `BINANCE_USE_TESTNET`) |
| `Infrastructure/BinanceRestClientProvider.cs` | Hot-swappable Binance client configuration |
| `Infrastructure/CashBalanceStateService.cs` | Mainnet reconciled cash snapshot cache |
| `Infrastructure/PositionTracker.cs` | In-memory open positions |
| `Infrastructure/PositionLifecycleManager.cs` | Entry→TP/SL→Close state machine per session |
| `Infrastructure/OrderRepository.cs` | **All SQL queries**: orders, reports (8h sessions), analytics |
| `Infrastructure/ReconciliationMetrics.cs` | Reconciliation cycle metrics and gauges |
| `Infrastructure/SessionBoundaryValidator.cs` | Session-boundary correction gating logic |
| `Infrastructure/BudgetRepository.cs` | Capital ledger (deposits, withdrawals, P&L) |
| `Infrastructure/AuditStreamPublisher.cs` | Publishes to Redis Streams `trades:audit` |
| `Infrastructure/OrderExecutionMetrics.cs` | OpenTelemetry metrics (phase 5: Prometheus) |
| `Infrastructure/SpreadFilterService.cs` | Rejects if bid-ask spread too wide |
| `Infrastructure/PriceConsensusService.cs` | Optional Bybit consensus pricing |
| `Configuration/Settings.cs` | SessionHours=8, Session phases, PartialTp, Binance API config |

**HTTP endpoints** (5094):
```
# Stats & Positions
GET  /api/trading/stats                        # Win/loss, Sharpe, drawdown
GET  /api/trading/positions                    # Open positions
GET  /api/trading/orders                       # Order history

# Reports (8-hour sessions)
GET  /api/trading/report/daily                 # Daily P&L
GET  /api/trading/report/daily/symbols         # Symbol breakdown
GET  /api/trading/report/time-analytics        # Holding time analysis
GET  /api/trading/report/hourly                # Hourly buckets
GET  /api/trading/report/sessions/daily        # 8h session daily report (3 sessions per day)
GET  /api/trading/report/sessions/range        # Multi-day session report
GET  /api/trading/report/sessions/{id}/symbols # Session symbols
GET  /api/trading/report/sessions/equity-curve # Equity curve

# Session
GET  /api/trading/session                      # Current session phase/time
GET  /api/trading/session/positions            # Session + open positions

# Budget
GET  /api/trading/budget/status                # Balance, initial capital
GET  /api/trading/budget/ledger                # Transaction history
POST /api/trading/budget/deposit               # Add capital
POST /api/trading/budget/withdraw              # Remove capital
GET  /api/trading/budget/equity-curve          # Balance over time
POST /api/trading/budget/reset                 # Reset to new balance

# Control
POST /api/trading/control/close-all            # Immediate close-all (requires token)
POST /api/trading/control/close-all/schedule   # Schedule for future UTC time
POST /api/trading/control/close-all/cancel     # Cancel scheduled
GET  /api/trading/control/close-all/status     # Current operation status (+discovery/attempt/verification/leftovers)
GET  /api/trading/control/close-all/history    # Past operations
POST /api/trading/control/resume               # Exit exit-only mode

# Reconciliation & Effective Balance
GET  /api/trading/reconciliation/health        # Reconciliation metrics health snapshot
GET  /api/trading/reconciliation/latest        # Latest reconciliation cycle drift details
GET  /api/trading/balance/effective            # Env-aware effective balance source payload

# Order pre-check behavior (runtime)
# REST + gRPC both enforce: amount/notional limits, spread/consensus checks,
# local sell-quantity guard, and buy cash/budget guard before placing exchange order.

# Runtime Config
POST /api/trading/reload-exchange-config       # Hot-swap Binance API keys (live/testnet)
POST /api/trading/validate-exchange            # Test Binance credentials
POST /api/trading/reload-trading-config        # Toggle live/testnet mode (via BINANCE_USE_TESTNET)

GET  /health
GET  /metrics                                   # Prometheus
```

---

### Notifier — `src/Services/Notifier/Notifier.Worker/`

**Role**: Telegram alerts for trade events

**Port**: HTTP=5095

| File | Purpose |
|------|---------|
| `Workers/NotifierWorker.cs` | Subscribes to Redis events |
| `Channels/TelegramNotifier.cs` | Telegram API client |
| `Services/NotificationBatcher.cs` | Deduplicates, batches before send |
| `Services/NotificationHistory.cs` | In-memory today's log |
| `Configuration/Settings.cs` | BotToken, ChatId |

**HTTP endpoints** (5095):
```
GET  /api/notifier/config          # Telegram config (masked)
GET  /api/notifier/stats           # Today's counts by category
POST /api/notifier/validate        # Test credentials (no save)
POST /api/notifier/reload-config   # Hot-swap token/chatId
POST /api/notifier/test-message    # Send test message
GET  /health
```

---

### HouseKeeper — `src/Services/HouseKeeper/HouseKeeper.Worker/`

**Role**: Scheduled DB cleanup

| File | Purpose |
|------|---------|
| `HouseKeeperWorker.cs` | Runs jobs on CRON schedule |
| `Jobs/DataGapsCleanupJob.cs` | Removes old gap records |
| `Jobs/OrdersArchiveJob.cs` | Archives old orders |
| `Jobs/PriceTicksPartitionJob.cs` | Creates yearly `price_ticks` partitions |
| `Jobs/UnusedTableAuditJob.cs` | Logs unused tables |
| `Configuration/HouseKeeperSettings.cs` | Schedule UTC, retention days |

---

### HistoricalCollector — `src/Services/HistoricalCollector/HistoricalCollector.Worker/`

**Role**: Backfill + gap-fill OHLCV data

| File | Purpose |
|------|---------|
| `Workers/HistoricalBackfillWorker.cs` | Bulk backfill from date range |
| `Workers/GapFillingWorker.cs` | Detects and fills gaps |
| `Infrastructure/BinanceVisionClient.cs` | Binance public archive downloader |
| `Infrastructure/HistoricalIngestionService.cs` | Processing pipeline |
| `Infrastructure/PriceTickBatchRepository.cs` | Batch DB inserts |
| `Parsers/KlineCsvParser.cs` | Binance CSV format parser |

---

### TimelineLogger — `src/Services/TimelineLogger/TimelineLogger.Worker/`

**Role**: Thu thập & lưu trữ toàn bộ sự kiện giao dịch theo từng symbol vào MongoDB

**Port**: HTTP=5096

**Storage**: MongoDB (`cryptotrading_timeline` DB, collections: `coin_events`, `event_summary`)

| File | Purpose |
|------|---------|
| `Program.cs` | Setup: MongoDB, Redis, REST endpoints |
| `Workers/CoinEventLoggerWorker.cs` | Subscribes `coin:*:log`, buffers → batch insert MongoDB |
| `Services/TimelineQueryService.cs` | Query engine: filter, paginate, summary, dashboard, export |
| `Infrastructure/MongoDbContext.cs` | MongoDB client, index initialization |
| `Infrastructure/CoinEventRepository.cs` | Insert/query `coin_events` collection |
| `Infrastructure/EventSummaryRepository.cs` | Insert/query `event_summary` collection |
| `Infrastructure/Documents/CoinEventDocument.cs` | MongoDB document model |
| `Infrastructure/Documents/EventSummaryDocument.cs` | Daily summary document model |
| `Configuration/MongoSettings.cs` | ConnectionString, Database, collection names |
| `Configuration/TimelineSettings.cs` | BatchSize, BatchTimeoutMs, DefaultRetentionDays |

**Event categories & retention**:

| Category | Event Types | Retention |
|----------|-------------|-----------|
| `PRICE_DATA` | PriceTickReceived | 7 days |
| `ANALYSIS` | IndicatorCalculated | 30 days |
| `TRADING_SIGNAL`, `STRATEGY`, `RISK`, `MARKET`, `NOTIFICATION` | Signal/Risk/Order events | 90 days |
| `TRADING`, `POSITION`, `LIQUIDATION`, `SESSION` | Order fills, positions, sessions | 365 days |

**HTTP endpoints** (5096):
```
GET  /api/timeline/events            # Filtered events (symbol required, supports date/time range, type, category, severity)
GET  /api/timeline/summary           # Daily summary or date-range summaries for a symbol
GET  /api/timeline/dashboard         # Aggregated stats across all symbols (default last 7 days)
GET  /api/timeline/export            # Export events as CSV or JSON
GET  /api/timeline/health            # Redis + processing stats
GET  /health
```

---

### FinancialLedger — `src/Services/FinancialLedger/FinancialLedger.Worker/`

**Role**: Virtual account ledger, session P&L tracking, real-time equity projection via SignalR

**Port**: HTTP=5097 + SignalR hub at `/ledger-hub`

**Storage**: PostgreSQL (ledger entries, sessions, accounts) + Redis (balance cache 30s TTL)

| File | Purpose |
|------|---------|
| `Program.cs` | WebApplication setup: SignalR, REST endpoints, bootstrap |
| `Configuration/LedgerSettings.cs` | DefaultInitialBalance, RedisEventsStreamKey, ExecutorUrl, EquityProjectionIntervalMs |
| `Domain/LedgerModels.cs` | DTOs: VirtualAccountDto, SessionDto, LedgerEntryDto, PnlBreakdown, ResetSessionRequest; LedgerEntryTypes enum |
| `Hubs/LedgerHub.cs` | SignalR hub — broadcasts ReceiveLedgerEntry, ReceiveBalanceUpdate, RealTimeEquity |
| `Infrastructure/LedgerRepository.cs` | PostgreSQL: InsertLedgerEntryAsync, GetCurrentBalanceAsync, GetLedgerEntriesAsync, GetPnlBySymbolAsync |
| `Infrastructure/VirtualAccountRepository.cs` | Account management: GetOrCreateAccountAsync, GetAccountAsync |
| `Services/PnlCalculationService.cs` | **CORE LOGIC**: balance caching (Redis 30s TTL), NetPnL, ROE% per session/symbol |
| `Services/SessionManagementService.cs` | Session lifecycle: CreateSessionAsync, ResetSessionAsync, GetActiveSessionAsync |
| `Services/SessionResetSagaService.cs` | Publishes HALT_AND_CLOSE_ALL to `trading-engine:commands` Redis channel |
| `Workers/TradeEventConsumerWorker.cs` | **CORE LOGIC**: Consumes `ledger:events` Redis Stream, inserts entries, broadcasts SignalR |
| `Workers/EquityProjectionWorker.cs` | Polls Executor positions + Redis mark prices every 5s, broadcasts RealTimeEquity via SignalR |

**Ledger entry types**: `INITIAL_FUNDING`, `REALIZED_PNL`, `COMMISSION`, `FUNDING_FEE`, `WITHDRAWAL`

**HTTP endpoints** (5097):
```
GET  /health
GET  /api/ledger/balance/effective            # Effective balance source for TESTNET consumers (RiskGuard)
GET  /api/ledger/account/{accountId}     # Balance, NetPnL, ROE% for active session
GET  /api/ledger/entries                 # Paginated entries (sessionId, fromDate, toDate, symbol, type, page, pageSize)
GET  /api/ledger/sessions/{accountId}    # All sessions for account (optional ?status=)
GET  /api/ledger/pnl                     # P&L breakdown by symbol (sessionId, optional symbol)
POST /api/ledger/sessions/reset          # Reset session (triggers HALT_AND_CLOSE_ALL + new session)
POST /api/ledger/accounts/bootstrap      # Create/get account and active session
```

**SignalR messages** (hub: `/ledger-hub`):
```
ReceiveLedgerEntry   → { sessionId, binanceTransactionId, type, amount, symbol, timestamp }
ReceiveBalanceUpdate → { sessionId, balance, timestamp }
RealTimeEquity       → { unrealizedPnl, realTimeEquity, positions: [{ symbol, quantity, entryPrice, markPrice, unrealizedPnl }] }
```

---

### Gateway — `src/Gateway/Gateway.API/`

**Role**: YARP reverse proxy + dashboard data + credential storage

**Port**: 5000

| File | Purpose |
|------|---------|
| `Program.cs` | Static files, HTTP clients, dashboard endpoints |
| `Dashboard/DashboardQueryService.cs` | Complex SQL for overview/candles |
| `Dashboard/SystemSettingsRepository.cs` | Encrypted Binance/Telegram credential storage |
| `Settings/DashboardOptions.cs` | Cache TTL, symbols, intervals |

**Routes to**: RiskGuard (5093), Notifier (5095), Executor (5094)

**HTTP endpoints** (5000):
```
GET /api/dashboard/overview            # Aggregated candles across symbols
GET /api/dashboard/candles             # OHLCV with downsampling
GET /api/dashboard/quality/coverage    # Data quality metrics
GET /api/trading/reconciliation/latest # Proxy to Executor reconciliation latest
```

---

## SHARED LIBRARY — `src/Shared/`

### DTOs

| File | Key Fields |
|------|-----------|
| `DTOs/PriceTick.cs` | Open, High, Low, Close, Volume, Time |
| `DTOs/TradeSignal.cs` | Symbol, RSI, EMA9/21, BB, Strength, MarketRegime, Atr, FundingRate |
| `DTOs/OrderRequest.cs` | Symbol, Side, Type, Qty, Price, StopLoss, TakeProfit, SessionId |
| `DTOs/OrderResult.cs` | OrderId, FilledQty, FilledPrice, Success |
| `DTOs/OrderSide.cs` | Buy / Sell |
| `DTOs/OrderType.cs` | Market / Limit / StopLimit |
| `DTOs/SignalStrength.cs` | Weak / Neutral / Strong |
| `DTOs/SessionPhase.cs` | Trading / SoftUnwind / LiquidationOnly / ForcedFlatten |
| `DTOs/MarketRegime.cs` | Choppy / TrendingUp / TrendingDown / HighVol |
| `DTOs/LiquidationReason.cs` | SessionEnd / DrawdownLimit / ... |
| `DTOs/RiskValidationResult.cs` | Rule evaluation result |
| `DTOs/RiskEvaluationDto.cs` | Audit trail for evaluations |
| `DTOs/RecoveryState.cs` | Recovery window state |
| `DTOs/TradingErrorCode.cs` | Extended error codes (includes insufficient position/cash and snapshot unavailable) |

### Session Management (8-hour Sessions)

| File | Purpose |
|------|---------|
| `Session/SessionClock.cs` | 8-hour session timing (S1: 00-08, S2: 08-16, S3: 16-24 UTC), phase detection |
| `Session/SessionInfo.cs` | Current session data, metadata |
| `Session/SessionTradingPolicy.cs` | Session business rules (liquidation, soft-unwind windows) |
| `Session/SessionSettings.cs` | Configuration: SessionHours=8, LiquidationWindowMinutes=30 |

### Other

| File | Purpose |
|------|---------|
| `Constants/RedisChannels.cs` | All channel name constants |
| `Extensions/SpanExtensions.cs` | Zero-alloc indicator math |
| `Database/PriceTicksTableHelper.cs` | Partition mapping SQL |
| `Json/TradingJsonContext.cs` | Source-generated JSON context |
| `ProtoFiles/order_executor.proto` | PlaceOrder RPC definition |
| `ProtoFiles/risk_guard.proto` | ValidateOrder RPC definition (`environment` added in request) |

---

## DATABASE

### PostgreSQL + TimescaleDB

**Port**: 5433 | **Access**: Npgsql + Dapper (raw SQL, no ORM) | **Config key**: `ConnectionStrings:Postgres`

| Table | Location | Purpose |
|-------|----------|---------|
| `price_ticks` | `y{YEAR}.price_ticks` (yearly partitions) | All OHLCV data |
| `orders` | default schema | All order records (live/testnet, no paper) |
| `ledger_entries` | default schema | Virtual account ledger (phase 4+) |
| `virtual_accounts` | default schema | Account & session management |
| `budget_ledger` | default schema | Capital deposits/withdrawals |
| `risk_evaluations` | default schema | Rule evaluation audit trail |
| `session_reports` | default schema | 8-hour session analytics (S1/S2/S3) |
| `system_settings` | default schema | Encrypted credentials |
| `trading_control_operations` | default schema | Close-all operation history |

**Migration scripts**: `scripts/` (SQL files + `run-migration.ps1` / `run-rollback.ps1`)

### MongoDB (TimelineLogger)

**Port**: 27017 | **Access**: MongoDB.Driver | **Config key**: `MongoDB:ConnectionString`
**Database**: `cryptotrading_timeline`

| Collection | Purpose |
|------------|---------|
| `coin_events` | All timeline events per symbol (TTL index on `expiresAt`) |
| `event_summary` | Daily aggregated summary per symbol |

---

## INFRASTRUCTURE

**Docker Compose**: `infrastructure/docker-compose.yml`
**Env file**: `infrastructure/.env` (copy from `.env.template`)

| Service | Ports | Depends On |
|---------|-------|-----------|
| postgres | 5433→5432 | — |
| redis | 6379 | — |
| mongodb | 27017 | — |
| riskguard | 5013, 5093 | postgres, redis |
| executor | 5014, 5094 | postgres, redis |
| notifier | 5095 | redis |
| analyzer | — | redis |
| ingestor | — | postgres, redis |
| strategy | — | redis |
| timelinelogger | 5096 | redis, mongodb |
| financialledger | 5097 | postgres, redis, executor |
| frontend-ledger | 5098→80 | financialledger |
| gateway | 5000 | all services |
| historicalcollector | — | postgres |
| housekeeper | — | postgres |

**Observability** (optional): `infrastructure/docker-compose-observability.yml` — Prometheus (5090), Grafana (3000)

**Important Docker wiring for RiskGuard effective balance**:
- `Executor__BaseUrl=http://trading_executor:5094`
- `FinancialLedger__BaseUrl=http://trading_financialledger:5097`

---

## ENVIRONMENT VARIABLES

Set in `infrastructure/.env`:

```env
# Exchange
BINANCE_API_KEY=
BINANCE_API_SECRET=
BINANCE_TESTNET_API_KEY=
BINANCE_TESTNET_API_SECRET=
BINANCE_USE_TESTNET=true

# Notifications
TELEGRAM_BOT_TOKEN=
TELEGRAM_CHAT_ID=0

# Database (PostgreSQL)
POSTGRES_USER=trader
POSTGRES_PASSWORD=strongpassword
POSTGRES_DB=cryptotrading

# Database (MongoDB — for TimelineLogger)
MONGO_USERNAME=timeline_user
MONGO_PASSWORD=strongpassword
MONGO_DATABASE=cryptotrading_timeline

# Trading (Live only)
INITIAL_BALANCE=10000.00

# Risk Rules
MAX_DRAWDOWN_PERCENT=5.0
MIN_RISK_REWARD=2.0
MAX_POSITION_SIZE_PERCENT=2.0
COOLDOWN_SECONDS=30

# Timeline Logger
TIMELINE_BATCH_SIZE=100
TIMELINE_BATCH_TIMEOUT_MS=5000
TIMELINE_DEFAULT_RETENTION_DAYS=90

# Maintenance
HOUSEKEEPER_ENABLED=true
HOUSEKEEPER_SCHEDULE_UTC=03:15
HOUSEKEEPER_RETENTION_ORDERS_DAYS=365
HOUSEKEEPER_RETENTION_TICKS_MONTHS=12

# Historical Backfill
HISTORICAL_BACKFILL_ENABLED=false
HISTORICAL_START_DATE=2025-01-01
HISTORICAL_END_DATE=2026-03-01

# Financial Ledger
FINANCIAL_LEDGER_DEFAULT_INITIAL_BALANCE=10000
FINANCIAL_LEDGER_REDIS_STREAM_KEY=ledger:events
FINANCIAL_LEDGER_REDIS_CONSUMER_GROUP=financial-ledger
```

---

## FRONTEND

### Main Dashboard — `frontend/`

**Stack**: React 18, Vite 5, Tailwind CSS 3, react-i18next 16, React Router 7
**Served via**: Gateway (port 5000)

### Pages

| File | Route | Purpose |
|------|-------|---------|
| `pages/OverviewPage.jsx` | `/` | Candles, market stats |
| `pages/TradingPage.jsx` | `/trading` | Open positions & orders |
| `pages/SafetyPage.jsx` | `/safety` | Risk status, evaluations |
| `pages/EventsPage.jsx` | `/events` | System event log |
| `pages/ReportPage.jsx` | `/report` | Daily/hourly P&L |
| `pages/SessionReportPage.jsx` | `/session-report` | 8-hour session analytics (3 sessions/day) |
| `pages/BudgetPage.jsx` | `/budget` | Capital ledger |
| `pages/SettingsPage.jsx` | `/settings` | Service config panels |
| `pages/ShutdownControlPage.jsx` | `/shutdown` | Close-all & exit-only |
| `pages/SymbolTimelinePage.jsx` | `/timeline` | Per-symbol history |
| `pages/GuidancePage.jsx` | `/guidance` | Help content |

### Key Components

| File | Purpose |
|------|---------|
| `components/settings/ExchangePanel.jsx` | Binance API config |
| `components/settings/RiskSettingsPanel.jsx` | Risk rule thresholds |
| `components/settings/TelegramPanel.jsx` | Telegram config |
| `components/settings/TradingModePanel.jsx` | Live/Testnet toggle (via BINANCE_USE_TESTNET env var) |
| `components/SystemHealthPanel.jsx` | Service health status |
| `components/SafetyLight.jsx` | Green/yellow/red indicator |
| `hooks/useDashboard.js` | Dashboard data fetching |
| `hooks/usePolling.js` | Polling utility |
| `context/SettingsContext.jsx` | Global settings state |

### i18n

- **Default**: Vietnamese (`vi`)
- **Secondary**: English (`en`)
- **Config**: `src/i18n/index.js`
- **Namespaces**: `common`, `navigation`, `overview`, `trading`, `safety`, `events`, `report`, `session-report`, `settings`, `shutdown`, `budget`, `timeline`, `guidance`
- **Locales path**: `src/i18n/locales/{vi,en}/{namespace}.json`

---

### Financial Ledger UI — `frontend-ledger/`

**Stack**: React 18, Vite 5, Tailwind CSS 3, react-i18next, SignalR (`@microsoft/signalr`)
**Port**: 5098 (nginx serving built assets + reverse proxy to FinancialLedger at 5097)

| File | Route | Purpose |
|------|-------|---------|
| `pages/LedgerPage.jsx` | `/` | Dashboard: balance, PnL, ROE%, open positions (real-time via SignalR) |
| `pages/EntriesPage.jsx` | `/entries` | Paginated ledger entries with filtering |
| `pages/SessionsPage.jsx` | `/sessions` | All sessions for account |
| `pages/PnLBreakdownPage.jsx` | `/pnl` | Per-symbol P&L breakdown |

| File | Purpose |
|------|---------|
| `hooks/useLedgerSignalR.js` | SignalR hook: ReceiveLedgerEntry, ReceiveBalanceUpdate, RealTimeEquity |
| `services/ledgerApi.js` | HTTP client for all `/api/ledger/*` endpoints |

### i18n (frontend-ledger)

- **Default**: Vietnamese (`vi`)
- **Secondary**: English (`en`)
- **Single namespace**: `ledger` (in `src/i18n/locales/{vi,en}/ledger.json`)

---

## HOW TO EXTEND

### Add a New Strategy

1. Implement `IStrategy` in `src/Services/Strategy/Strategy.Worker/Strategies/`
2. Register in `Strategy.Worker/Program.cs`
3. Strategies receive `TradeSignal` from Redis, emit `OrderRequest` via gRPC to RiskGuard

### Add a New Risk Rule

1. Implement `IRiskRule` in `src/Services/RiskGuard/RiskGuard.API/Rules/`
2. Register in `RiskGuard.API/Program.cs` (order matters — rules run sequentially, first reject wins)

### Add a New API Endpoint

- **Executor** or **RiskGuard**: Add to the respective `Program.cs` minimal API section or controller
- **Gateway**: Add proxy route in `Gateway.API/Program.cs` with appropriate `HttpClient`

### Add a New Indicator

1. Update `AnalyzerSettings.cs` for configuration
2. Add calculation logic to `Analysis/IndicatorEngine.cs`
3. Add field to `DTOs/TradeSignal.cs` in Shared library (regenerate gRPC stubs if needed)
