# CLAUDE.md — AI Navigation Guide

> Quick-reference for AI agents. Optimized for token efficiency and fast context loading.

---

## QUICK COMMANDS

```bash
dotnet build                                    # Build all
dotnet build src/Services/Executor/Executor.API/Executor.API.csproj
dotnet test
dotnet test --filter "ClassName=RsiCalculatorTests"
dotnet test --filter "FullyQualifiedName~RsiCalculatorTests.Calculate_ReturnsExpectedValue"
dotnet test /p:CollectCoverage=true
cd infrastructure && docker compose up -d redis postgres
cd infrastructure && docker compose up -d
cd infrastructure && docker compose build executor
cd infrastructure && docker compose logs -f strategy
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
```

### Service Types

| Type | Builder | Services |
|------|---------|----------|
| BackgroundService workers | `Host.CreateApplicationBuilder` | Ingestor, Analyzer, Strategy, Notifier, HistoricalCollector, HouseKeeper |
| gRPC API servers | `WebApplication.CreateBuilder` | RiskGuard (5013/5093), Executor (5014/5094) |
| Web API workers | `WebApplication.CreateBuilder` | TimelineLogger (5096) |
| Web gateway | `WebApplication.CreateBuilder` | Gateway.API (5000) |

### Binance Testnet Scope

> **Testnet chỉ áp dụng cho Executor** (đặt lệnh thực thi). Tất cả service còn lại dùng Binance **Live** API.

| Service | Binance Environment | Lý do |
|---------|--------------------|----|
| **Executor** | Live hoặc **Testnet** (theo `BINANCE_USE_TESTNET`) | Đặt/huỷ lệnh thực — cần testnet để test an toàn |
| **Ingestor** | **Live** (luôn luôn) | Binance testnet không cung cấp WebSocket market data (kline/ticker) |
| Analyzer, Strategy, RiskGuard, Notifier | Không gọi Binance trực tiếp | Chỉ dùng Redis pub/sub và gRPC nội bộ |

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
POST /api/risk/reload-config            # Hot-swap settings
GET  /api/risk/stats                    # Today's validations, drawdown
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
| `Services/OrderExecutionService.cs` | **CORE LOGIC**: Routes to BinanceOrderClient or PaperOrderSimulator |
| `Services/LiquidationOrchestrator.cs` | Closes positions at session end |
| `Services/PartialTpMonitorService.cs` | Multi-stage take-profit |
| `Services/StartupReconciliationService.cs` | Syncs memory↔DB on startup |
| `Services/ShutdownOperationService.cs` | Tracks close-all, exit-only mode |
| `Services/CloseAllExecutorService.cs` | Parallel order cancellation |
| `Services/SessionExitOnlyMonitorService.cs` | Enforces exit-only in final session minutes |
| `Services/RecoveryStateService.cs` | Tracks post-drawdown recovery window |
| `Infrastructure/BinanceOrderClient.cs` | Live Binance order placement (Market/Limit/StopLimit) |
| `Infrastructure/BinanceRestClientProvider.cs` | Hot-swappable Binance client |
| `Infrastructure/PaperOrderSimulator.cs` | Simulated fills with slippage |
| `Infrastructure/PositionTracker.cs` | In-memory open positions |
| `Infrastructure/PositionLifecycleManager.cs` | Entry→TP/SL→Close state machine |
| `Infrastructure/OrderRepository.cs` | **All SQL queries**: orders, reports, sessions, analytics |
| `Infrastructure/BudgetRepository.cs` | Capital ledger (deposits, withdrawals, P&L) |
| `Infrastructure/AuditStreamPublisher.cs` | Publishes to Redis Streams `trades:audit` |
| `Infrastructure/OrderExecutionMetrics.cs` | OpenTelemetry metrics |
| `Infrastructure/SpreadFilterService.cs` | Rejects if bid-ask spread too wide |
| `Infrastructure/PriceConsensusService.cs` | Optional Bybit consensus pricing |
| `Configuration/Settings.cs` | PaperTradingMode, Session, PartialTp, ConsensusPricing |

**HTTP endpoints** (5094):
```
# Stats & Positions
GET  /api/trading/stats                        # Win/loss, Sharpe, drawdown
GET  /api/trading/positions                    # Open positions
GET  /api/trading/orders                       # Order history

# Reports
GET  /api/trading/report/daily                 # Daily P&L
GET  /api/trading/report/daily/symbols         # Symbol breakdown
GET  /api/trading/report/time-analytics        # Holding time analysis
GET  /api/trading/report/hourly                # Hourly buckets
GET  /api/trading/report/sessions/daily        # 4h session daily report
GET  /api/trading/report/sessions/range        # Multi-day session report
GET  /api/trading/report/sessions/{id}/symbols # Session symbols
GET  /api/trading/report/sessions/equity-curve # Equity curve
GET  /api/trading/report/capital-flow          # P&L live vs paper

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
GET  /api/trading/control/close-all/status     # Current operation status
GET  /api/trading/control/close-all/history    # Past operations
POST /api/trading/control/resume               # Exit exit-only mode

# Runtime Config
POST /api/trading/reload-exchange-config       # Hot-swap Binance API keys
POST /api/trading/validate-exchange            # Test Binance credentials
POST /api/trading/reload-trading-config        # Toggle paper/live mode

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

### Session Management

| File | Purpose |
|------|---------|
| `Session/SessionClock.cs` | 4h session timing, phase detection |
| `Session/SessionInfo.cs` | Current session data |
| `Session/SessionTradingPolicy.cs` | Session business rules |
| `Session/SessionSettings.cs` | Configuration |

### Other

| File | Purpose |
|------|---------|
| `Constants/RedisChannels.cs` | All channel name constants |
| `Extensions/SpanExtensions.cs` | Zero-alloc indicator math |
| `Database/PriceTicksTableHelper.cs` | Partition mapping SQL |
| `Json/TradingJsonContext.cs` | Source-generated JSON context |
| `ProtoFiles/order_executor.proto` | PlaceOrder RPC definition |
| `ProtoFiles/risk_guard.proto` | ValidateOrder RPC definition |

---

## DATABASE

### PostgreSQL + TimescaleDB

**Port**: 5433 | **Access**: Npgsql + Dapper (raw SQL, no ORM) | **Config key**: `ConnectionStrings:Postgres`

| Table | Location | Purpose |
|-------|----------|---------|
| `price_ticks` | `y{YEAR}.price_ticks` (yearly partitions) | All OHLCV data |
| `orders` | default schema | All order records |
| `budget_ledger` | default schema | Capital deposits/withdrawals |
| `risk_evaluations` | default schema | Rule evaluation audit trail |
| `session_reports` | default schema | 4h session analytics |
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
| gateway | 5000 | all services |
| historicalcollector | — | postgres |
| housekeeper | — | postgres |

**Observability** (optional): `infrastructure/docker-compose-observability.yml` — Prometheus (5090), Grafana (3000)

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

# Trading
PAPER_TRADING_MODE=true
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
```

---

## FRONTEND — `frontend/`

**Stack**: React 18, Vite 5, Tailwind CSS 3, react-i18next 16, React Router 7

### Pages

| File | Route | Purpose |
|------|-------|---------|
| `pages/OverviewPage.jsx` | `/` | Candles, market stats |
| `pages/TradingPage.jsx` | `/trading` | Open positions & orders |
| `pages/SafetyPage.jsx` | `/safety` | Risk status, evaluations |
| `pages/EventsPage.jsx` | `/events` | System event log |
| `pages/ReportPage.jsx` | `/report` | Daily/hourly P&L |
| `pages/SessionReportPage.jsx` | `/session-report` | 4h session analytics |
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
| `components/settings/TradingModePanel.jsx` | Paper/Live toggle |
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
