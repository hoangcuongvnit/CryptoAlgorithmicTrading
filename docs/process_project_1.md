# CryptoAlgorithmicTrading - Service Scan and Business Logic

## 1) Solution Goal (Business Task)

This solution is a microservice-based crypto trading platform that automates the full lifecycle of algorithmic trading:

1. Ingest real-time and historical market data.
2. Analyze price action and compute technical indicators.
3. Convert signals into trade intents.
4. Apply strict risk controls before execution.
5. Execute orders in paper mode or live mode.
6. Persist all events/orders for auditability and observability.
7. Notify operators in real time (Telegram + dashboard APIs).

In business terms, the system is designed to run continuously, reduce manual trading decisions, enforce risk discipline, and provide traceability of all decisions.

## 2) End-to-End Runtime Flow

1. `DataIngestor` receives Binance mini-ticker + 1m kline updates.
2. It publishes `PriceTick` to Redis channel `price:{symbol}` and queues data for PostgreSQL persistence.
3. `Analyzer` subscribes to `price:*`, keeps rolling buffers, computes RSI/EMA/Bollinger, and publishes `TradeSignal` to `signal:{symbol}`.
4. `Strategy` subscribes to `signal:*`, maps signal -> `OrderRequest`, calls `RiskGuard` via gRPC.
5. `RiskGuard` executes ordered risk rules, either rejects or approves (optionally with adjusted quantity).
6. If approved, `Strategy` sends order to `Executor` via gRPC.
7. `Executor` validates, applies safety settings, executes paper/live, persists to `orders`, and writes audit event to Redis Stream `trades:audit`.
8. `Notifier` consumes `system:events` Pub/Sub and `trades:audit` stream, sends Telegram notifications, and exposes REST stats.
9. `Gateway.API` serves dashboard endpoints (candles, quality, schema, workbench templates) from PostgreSQL and proxies Risk/Notifier management endpoints.

## 3) Shared Contract Layer (`src/Shared`)

Core contracts used across all services:

- `PriceTick`: symbol, OHLCV, timestamp, interval.
- `TradeSignal`: RSI, EMA9/EMA21, Bollinger bands, strength, timestamp.
- `OrderRequest`: side/type/qty/price/SL/TP/strategy name.
- `OrderResult`: success/failure, fill info, paper/live flag.
- `SystemEvent`: service and infrastructure events.
- Redis channel conventions in `RedisChannels`:
	- `price:{symbol}`
	- `signal:{symbol}`
	- `system:events`
	- `trades:audit`
	- `config:symbols`

gRPC contracts:

- `risk_guard.proto`: `ValidateOrder` request/reply.
- `order_executor.proto`: `PlaceOrder` request/reply.

## 4) Service-by-Service Logic Scan

### 4.1 DataIngestor (`src/Services/DataIngestor/Ingestor.Worker`)

Business role:

- Market data acquisition and fan-out.
- First producer of live trading data.

Main logic:

- `BinanceIngestorWorker` subscribes for each configured symbol to:
	- Mini ticker updates (fast price context; interval=`ticker`).
	- 1m kline close events (only final candles).
- Every tick is:
	- Published to Redis via `RedisPublisher` (`price:{symbol}`).
	- Enqueued to bounded channel `PriceTickWriteQueue`.
- `PriceTickPersistenceWorker` flushes queue in batches/time windows and persists via `PriceTickRepository`.
- Repository inserts by year-partitioned tables through `PriceTicksTableHelper`.
- `SymbolConfigListener` listens to `config:symbols` updates (currently logs only; dynamic resubscribe is TODO).
- Publishes startup/shutdown/connection-lost/connection-restored/error events to `system:events`.

Operational intent:

- Maintain continuous feed with resilience signals.
- Decouple ingestion speed from DB writes using a bounded queue.

### 4.2 Analyzer (`src/Services/Analyzer/Analyzer.Worker`)

Business role:

- Transform raw candles into actionable quantitative signals.

Main logic:

- `SignalAnalyzerWorker` subscribes to `price:*` pattern.
- Filters only configured candle interval (`Analyzer:SignalInterval`, default `1m`); ignores `ticker` updates.
- `PriceBuffer` maintains per-symbol rolling quote buffers with minute deduplication.
- `IndicatorEngine` computes:
	- RSI (default 14)
	- EMA short/long (default 9/21)
	- Bollinger Bands (default 20, 2 stddev)
- `EvaluateStrength` creates `Weak/Moderate/Strong` by confirmation logic:
	- Bullish branch: oversold RSI and/or price near lower band.
	- Bearish branch: overbought RSI and/or price near upper band.
- `SignalPublisher` publishes resulting `TradeSignal` to `signal:{symbol}`.

Operational intent:

- Keep signal generation deterministic and symbol-isolated.
- Avoid signal emission until enough lookback candles exist.

### 4.3 Strategy (`src/Services/Strategy/Strategy.Worker`)

Business role:

- Decision bridge between analytics and order execution.

Main logic:

- `Worker` subscribes to `signal:*`.
- Deserializes each `TradeSignal` and maps it via `SignalToOrderMapper`.
- Mapper rules:
	- Enforces minimum signal strength threshold.
	- Direction from EMA regime (`ema9 >= ema21` => Buy else Sell).
	- Entry from BB middle (fallback EMA9).
	- SL/TP heuristics (Buy: -1.5%/+3%, Sell inverse).
	- Uses `OrderType.Market` and configured default quantity.
- Sends mapped order to `RiskGuard.ValidateOrder` (gRPC).
- On approved response, applies adjusted quantity if provided.
- Sends to `Executor.PlaceOrder` (gRPC).
- Logs reject/execute/failure outcomes.

Operational intent:

- Keep strategy stateless and fast.
- Separate alpha logic (signal mapping) from risk/execution concerns.

### 4.4 RiskGuard (`src/Services/RiskGuard/RiskGuard.API`)

Business role:

- Centralized pre-trade risk gate (hard safety boundary).

Main logic:

- `RiskGuardGrpcService` exposes `ValidateOrder`.
- `RiskValidationEngine` executes ordered chain of `IRiskRule`; first reject short-circuits.
- Quantity adjustments from rules propagate to next rules and to final response.
- Validation decisions are tracked in in-memory `ValidationHistory`.

Rule chain currently registered in this order:

1. `SymbolAllowListRule` - reject if symbol not allowed.
2. `QuantityRule` - reject invalid qty/entry.
3. `PositionSizeRule` - cap notional by percent-of-balance (or fallback max notional), adjust qty down.
4. `RiskRewardRule` - reject if RR below minimum.
5. `CooldownRule` - reject rapid repeat trades per symbol.
6. `MaxDrawdownRule` - reject all when daily net loss breaches configured threshold.

Supporting infrastructure:

- `OrderStatsRepository` queries `orders` table for daily net PnL (`sell cashflow - buy cashflow`).
- `SystemEventPublisher` emits `MaxDrawdownBreached` once/day to `system:events`.
- REST endpoints for operations:
	- `/api/risk/config`
	- `/api/risk/stats`
	- `/health`

Operational intent:

- Enforce non-negotiable risk controls independently of strategy quality.
- Fail-open for monitoring-only dependencies (DB read errors do not block trading in drawdown rule).

### 4.5 Executor (`src/Services/Executor/Executor.API`)

Business role:

- Final order execution engine with audit and metrics.

Main logic:

- `OrderExecutorGrpcService.PlaceOrder` validates input and execution guardrails:
	- Required symbol/side/type/quantity rules.
	- Side-consistent SL/TP geometry.
	- Global kill switch.
	- Allowed symbols list.
	- Max notional per order.
- Execution path:
	- `PaperTradingMode=true` => `PaperOrderSimulator`.
		- Uses request price or latest DB close (`PriceReferenceRepository`).
		- Applies configurable slippage bps.
	- `PaperTradingMode=false` => `BinanceOrderClient`.
		- Places spot orders through Binance REST client.
		- Protected by retry + circuit breaker (Polly).
- Post-execution always attempts:
	- Persist order to PostgreSQL via `OrderRepository`.
	- Publish audit event to Redis Stream `trades:audit` via `AuditStreamPublisher`.
- OpenTelemetry metrics recorded through `OrderExecutionMetrics`.

REST endpoints:

- `/health`
- `/metrics` (message placeholder; OTEL exporters configured)

Operational intent:

- Guarantee every execution attempt has durable database + stream trace.
- Support paper/live mode switch without strategy changes.

### 4.6 Notifier (`src/Services/Notifier/Notifier.Worker`)

Business role:

- Human-facing alerting and operational awareness.

Main logic:

- On startup:
	- Tests Telegram connectivity.
	- Sends startup notification.
- Subscribes to Pub/Sub `system:events`:
	- Formats events by type (service lifecycle, WS issues, drawdown breach, errors).
	- Sends to Telegram via `TelegramNotifier`.
- Polls Redis Stream `trades:audit` in loop:
	- Parses stream entries into `OrderResult`.
	- Sends execution/rejection notifications.
- Tracks local notification counters/history in `NotificationHistory`.
- Exposes REST ops endpoints:
	- `/api/notifier/config`
	- `/api/notifier/stats`
	- `/health`

Operational intent:

- Keep operator informed in near-real time, even when no dashboard is open.

### 4.7 HistoricalCollector (`src/Services/HistoricalCollector/HistoricalCollector.Worker`)

Business role:

- Data completeness engine for backtests/analytics/dashboard quality.

Main logic:

- Two modes:
	- Backfill mode (`HistoricalData:Enabled=true`): one-time ingest over configured date range.
	- Gap-filling mode (`GapFilling:Enabled=true`, and backfill disabled): daily schedule scans/fills missing data.
- `HistoricalIngestionService`:
	- Downloads monthly Binance Vision ZIP files with `BinanceVisionClient`.
	- Parses CSV rows into `PriceTick` (`KlineCsvParser`).
	- Filters by target ingest window.
	- Batch upserts into yearly partitioned tables via `PriceTickBatchRepository`.
- `PriceTickBatchRepository` also:
	- Detects missing days/candle deficits.
	- Stores open gaps in `historical_collector.data_gaps`.
	- Marks gaps as filled after successful re-ingestion.

Operational intent:

- Ensure historical dataset integrity for analytics and UI.
- Recover from outages or missing files automatically over time.

### 4.8 Gateway.API (`src/Gateway/Gateway.API`)

Business role:

- Dashboard backend and management API aggregation layer.

Main logic:

- Serves static dashboard assets + JSON APIs.
- `DashboardQueryService` queries PostgreSQL (`historical_collector` schema) with in-memory caching.
- Key endpoints:
	- `/api/dashboard/overview` - per-symbol coverage/freshness/open-gap summary.
	- `/api/dashboard/candles` - OHLCV with server-side interval downsampling (`1m` -> `5m/15m/1h/1d` by range).
	- `/api/dashboard/quality/coverage` - expected vs actual candles.
	- `/api/dashboard/quality/gaps` - paged gap list with duration/fill latency.
	- `/api/dashboard/schema` - table/column/index/constraint explorer.
	- `/api/dashboard/workbench/template/{templateId}` - predefined SQL templates (`volatile-days`, `monthly-volume`, `missing-data-report`).
- Proxies operational endpoints:
	- `/api/risk/*` -> RiskGuard.
	- `/api/notifier/*` -> Notifier.

Operational intent:

- Provide a single UI/API entry point for data quality and service operations.

## 5) Business Logic Highlights Across the Solution

1. Separation of concerns:
	 - Signal generation, risk control, and execution are isolated services.
2. Risk-first order path:
	 - No order can reach executor without passing `RiskGuard`.
3. Paper-to-live readiness:
	 - Same contracts and flow; execution backend switches by config.
4. Observability and auditability:
	 - Orders persisted to DB and mirrored to `trades:audit` stream.
	 - System and risk incidents broadcast via `system:events`.
5. Data quality as a first-class feature:
	 - Historical backfill + gap detection/filling + dashboard quality endpoints.

## 6) Current Practical Notes (from code scan)

1. Dynamic symbol reconfiguration is not fully implemented yet in `SymbolConfigListener` (currently logs updates only).
2. Cooldown memory in `RiskGuard` is in-process and resets on service restart.
3. Max drawdown check depends on `orders` table cashflow approximation, not mark-to-market unrealized PnL.
4. Notifier polls Redis Stream starting from `$`, so it processes newly arriving audit events during runtime.

## 7) Short Executive Summary

This system implements a full algorithmic trading pipeline with explicit safety and traceability boundaries:

- `DataIngestor + Analyzer` produce structured trading opportunities.
- `Strategy + RiskGuard` transform opportunities into risk-compliant orders.
- `Executor` performs paper/live execution and durable audit logging.
- `Notifier + Gateway` provide operational visibility and control.

Overall, the solution is built to automate trading decisions while preserving strong risk governance, historical data integrity, and operational observability.
