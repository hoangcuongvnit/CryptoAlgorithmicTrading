# Architecture Overview

This document describes the current system design of CryptoAlgorithmicTrading. It is written for maintainers and AI coding agents that need the actual runtime model of the repository before changing code.

## System Goal

The platform is a live crypto trading system built as a monorepo of independently deployable services. The design favors low-latency market data propagation, strict pre-trade validation, durable auditability, and clear separation between market execution, risk control, and ledger/reporting concerns.

## High-Level Topology

```text
Binance WebSocket
      |
      v
Ingestor.Worker ---- Redis price:{SYMBOL} ----> Analyzer.Worker ---- Redis signal:{SYMBOL}
							 |
							 v
						 Strategy.Worker
							 |
							 v
					      RiskGuard.API (gRPC)
							 |
						approved orders only
							 v
						Executor.API (gRPC + REST)
							 |
				 +-----------------------+------------------------+
				 |                        |                        |
				 v                        v                        v
			 PostgreSQL / TimescaleDB     Redis Streams          Redis / MongoDB
			 orders, sessions, reports    trades:audit           timeline, ledger
```

## Service Map

| Service | Role | Primary Ports |
|---|---|---|
| `Ingestor.Worker` | Binance market data ingestion and Redis publishing | worker only |
| `Analyzer.Worker` | Technical indicator calculation and signal generation | worker only |
| `Strategy.Worker` | Signal-to-order mapping and RPC orchestration | worker only |
| `RiskGuard.API` | Sequential risk validation and effective-balance checks | gRPC 5013, HTTP 5093 |
| `Executor.API` | Exchange order execution, reconciliation, and control flows | gRPC 5014, HTTP 5094 |
| `Notifier.Worker` | Telegram notifications and health alerts | HTTP 5095 |
| `TimelineLogger.Worker` | Symbol timeline persistence and query support | HTTP 5096 |
| `FinancialLedger.Worker` | Ledger sessions, balance reporting, and equity projection | HTTP 5097, SignalR `/ledger-hub` |
| `Gateway.API` | Dashboard proxy and web aggregation | HTTP 5000 |
| `HouseKeeper.Worker` | Cleanup and partition maintenance | worker only |
| `HistoricalCollector.Worker` | Historical backfill and gap fill | worker only |

## Communication Protocols

### Redis Pub/Sub

Redis Pub/Sub carries the latency-sensitive signal chain:

- `price:{SYMBOL}` from Ingestor to Analyzer.
- `signal:{SYMBOL}` from Analyzer to Strategy.
- `coin:{SYMBOL}:log` for timeline events.
- `executor:trading:mode` for trading mode broadcasts.
- `system:config:changed` for shared configuration updates.

### gRPC

gRPC is used for the control path between Strategy, RiskGuard, and Executor.

- `ValidateOrder` must include environment context so RiskGuard can resolve the correct effective-balance source.
- `PlaceOrder` is the final order execution contract.
- All gRPC services require unencrypted HTTP/2 support in local and containerized development.

### Redis Streams

Redis Streams provide durable event delivery for audit and inter-service event consumption.

- `trades:audit` stores execution audit data.
- `ledger:events` feeds FinancialLedger session accounting.
- `trading-engine:commands` carries control commands such as halt-and-close-all.

### HTTP APIs

HTTP is used for operational endpoints, dashboards, status, and read-heavy queries.

## Session Model

The repository uses two distinct session concepts.

### Trading Sessions

Trading sessions are fixed 8-hour UTC cycles used by the trading engine.

- S1: 00:00 to 08:00 UTC
- S2: 08:00 to 16:00 UTC
- S3: 16:00 to 24:00 UTC

The final 30 minutes of each session are reserved for liquidation-oriented behavior and reduced risk-taking.

### Ledger Sessions

Ledger sessions are user-created reporting scopes in FinancialLedger.

- They begin when a reset or create action is submitted.
- They end only when a new ledger session is created.
- They can span multiple days or months.
- They are independent from trading sessions.

Do not conflate ledger-session reporting with trading-session boundaries when changing reporting logic, UI labels, or validation flows.

## Order Lifecycle

1. Binance market data is ingested and published to Redis.
2. Analyzer computes indicators and emits a trade signal.
3. Strategy maps the signal into an order request.
4. RiskGuard validates the request through a fixed rule chain.
5. Executor performs pre-checks, routes the order to Binance, and records the result.
6. Audit streams, reconciliation services, and ledger consumers persist the aftermath.

## RiskGuard Design

RiskGuard is intentionally sequential. The rule order matters because the first rejection wins and short-circuits the rest of the chain.

Current rule order:

1. ExitOnlyRule
2. RecoveryWindowRule
3. NoCrossSessionCarryRule
4. SessionWindowRule
5. SymbolAllowListRule
6. QuantityRule
7. PositionSizeRule
8. RiskRewardRule
9. CooldownRule
10. MaxDrawdownRule

RiskGuard resolves effective balance using the active environment:

- TESTNET -> FinancialLedger `/api/ledger/balance/effective`
- MAINNET -> Executor `/api/trading/balance/effective`

The compatibility fallback to virtual balance is controlled by `Risk:AllowVirtualBalanceFallback`.

## Executor Design

Executor owns exchange-side order placement and the protective runtime checks that happen before a request reaches Binance.

Pre-execution checks currently include:

- amount and notional validation
- spread filtering
- optional consensus pricing
- sell-side local quantity sufficiency checks
- buy-side cash or budget sufficiency checks

Executor also owns:

- close-all orchestration and post-close verification
- startup reconciliation with Binance
- periodic reconciliation and drift logging
- cash snapshot state for mainnet trading
- order and session reporting

## Storage Layout

### PostgreSQL / TimescaleDB

PostgreSQL stores the core transactional and analytical data.

- orders
- session reports
- trading control operations
- risk evaluations
- ledger entries
- virtual accounts
- budget ledger
- reconciliation-related state

Time-series price data is partitioned through TimescaleDB conventions.

### MongoDB

TimelineLogger stores symbol-level event history in MongoDB for query and dashboard use.

- `coin_events`
- `event_summary`

### Redis

Redis is used as low-latency cache and transport infrastructure.

- market prices
- signals
- cooldown and control state
- balance caching
- audit stream processing

## Reconciliation And Safety

The system does not trust local state blindly.

- Executor performs startup reconciliation to rehydrate local positions and cash snapshots.
- Periodic reconciliation detects drift between Binance and internal state.
- Close-all flows discover, merge, execute, and verify positions rather than relying on a single local snapshot.
- Ledger and RiskGuard can fall back when data is stale, but that fallback is a controlled compatibility path, not the primary behavior.

## Observability

The repo exposes operational health through service-specific HTTP endpoints and metrics endpoints where available.

- RiskGuard exposes configuration, stats, and persistence health.
- Executor exposes trading stats, positions, order history, reconciliation health, and metrics.
- FinancialLedger exposes balance, entry, session, and PnL endpoints.
- TimelineLogger exposes event summary and health endpoints.
- Gateway aggregates dashboard-oriented data for the frontend.

## Deployment Model

The system is designed to run locally through Docker Compose or as separate services during development.

- Redis, PostgreSQL, and MongoDB provide the core infrastructure.
- Service DNS names must be used inside containers when one service calls another service.
- RiskGuard should not call `localhost` inside Docker Compose for effective-balance lookups.

## Source Of Truth

For implementation details, use the current codebase and [CLAUDE.md](CLAUDE.md) as the canonical implementation reference.

For agent behavior and repository-specific coding rules, use [.github/copilot-instructions.md](.github/copilot-instructions.md).
