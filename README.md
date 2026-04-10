# Crypto Algorithmic Trading System

A microservices-based cryptocurrency trading platform built on .NET 10. The repository is optimized for low-latency market data flow, guarded execution, ledger reporting, and long-running autonomous operation around Binance.

## Overview

The system is organized as a monorepo with separate services for market ingestion, signal analysis, strategy selection, risk validation, order execution, notification, timeline logging, and financial ledger management. Services communicate primarily through Redis Pub/Sub, gRPC, Redis Streams, PostgreSQL, Redis, and MongoDB.

```text
Binance WebSocket -> Ingestor -> Redis price:{SYMBOL} -> Analyzer -> Redis signal:{SYMBOL}
                                                     -> Strategy -> RiskGuard -> Executor
                                                     -> Audit/Timeline/Ledger/Notifier
```

## What This Repo Contains

- `src/Services/DataIngestor/Ingestor.Worker` for Binance market data ingestion.
- `src/Services/Analyzer/Analyzer.Worker` for indicator calculation and signal generation.
- `src/Services/Strategy/Strategy.Worker` for signal-to-order mapping.
- `src/Services/RiskGuard/RiskGuard.API` for sequential risk-rule validation.
- `src/Services/Executor/Executor.API` for order execution, reconciliation, and control flows.
- `src/Services/Notifier/Notifier.Worker` for Telegram notifications.
- `src/Services/TimelineLogger/TimelineLogger.Worker` for symbol-level event history in MongoDB.
- `src/Services/FinancialLedger/FinancialLedger.Worker` for virtual account, session, and equity reporting.
- `src/Gateway/Gateway.API` for dashboard routing and web aggregation.
- `src/Shared` for shared DTOs, proto contracts, constants, and session helpers.

## Core Runtime Facts

- The system currently uses 3 fixed 8-hour trading sessions per day: S1 00:00-08:00 UTC, S2 08:00-16:00 UTC, and S3 16:00-24:00 UTC.
- Trading sessions are not the same as ledger sessions. Trading sessions are fixed engine cycles; ledger sessions are user-created reporting scopes that can span multiple days.
- Paper trading has been removed. The system is live-only, with Executor able to switch between live and testnet mode through `BINANCE_USE_TESTNET`.
- RiskGuard resolves effective balance by environment: TESTNET uses FinancialLedger, MAINNET uses Executor reconciled balance.
- Close-all, reconciliation, and session hardening are first-class runtime behaviors, not future ideas.

## Technical Stack

| Area | Technology | Notes |
|---|---|---|
| Runtime | .NET 10 | File-scoped namespaces, nullable enabled |
| Messaging | Redis Pub/Sub, Redis Streams | Low-latency signals and durable audit/events |
| RPC | gRPC + Protobuf | Strategy, RiskGuard, and Executor contracts |
| Storage | PostgreSQL / TimescaleDB | Orders, sessions, ledger, reconciliation, reports |
| Document store | MongoDB | Timeline event history |
| Execution | Binance REST / WebSocket | Executor owns order placement |
| UI / Gateway | YARP | Dashboard proxy and API aggregation |

## Key Documentation

- [ARCHITECTURE.md](ARCHITECTURE.md) for the technical design and service topology.
- [CLAUDE.md](CLAUDE.md) for current implementation facts used by AI assistants.
- [docs/PHASE4_RUNBOOK.md](docs/PHASE4_RUNBOOK.md) for operational startup and verification.
- [docs/PHASE5_OBSERVABILITY.md](docs/PHASE5_OBSERVABILITY.md) for metrics and observability.

## Getting Started

1. Install the .NET 10 SDK and Docker Desktop.
2. Configure `infrastructure/.env` with Binance, Telegram, PostgreSQL, MongoDB, and trading settings.
3. Start the core infrastructure from `infrastructure/`.
4. Run the services you need, or use Docker Compose to start the stack.

```bash
cd infrastructure
docker compose up -d
```

For service-specific development, use the standard .NET build and test commands documented in [CLAUDE.md](CLAUDE.md).

## Engineering Principles

- Keep hot paths lean and allocation-conscious.
- Prefer explicit service boundaries over shared mutable state.
- Treat order validation, reconciliation, and reporting as part of the trading system, not auxiliary tooling.
- Preserve the established session terminology when adding new code or documentation.

## Contributing

Follow the existing repo structure, update tests when behavior changes, and keep implementation details aligned with the architecture and AI instructions in this repository.

## Disclaimer

This software is for educational and research purposes only. Cryptocurrency trading carries significant financial risk, and past performance does not guarantee future results.
