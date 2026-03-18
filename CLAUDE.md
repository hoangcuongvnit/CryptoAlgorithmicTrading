# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
# Build entire solution
dotnet build

# Build a specific service
dotnet build src/Services/Executor/Executor.API/Executor.API.csproj

# Run all tests
dotnet test

# Run a single test class or method
dotnet test --filter "ClassName=RsiCalculatorTests"
dotnet test --filter "FullyQualifiedName~RsiCalculatorTests.Calculate_ReturnsExpectedValue"

# Run tests with coverage
dotnet test /p:CollectCoverage=true

# Start infrastructure (Redis + PostgreSQL) only
cd infrastructure && docker compose up -d redis postgres

# Start all services via Docker
cd infrastructure && docker compose up -d

# Build a specific Docker service image
cd infrastructure && docker compose build executor

# View logs
cd infrastructure && docker compose logs -f strategy
```

## Project Configuration

- **SDK**: .NET 10.0 (pinned in `global.json` to `10.0.103`)
- **Target Framework**: `net10.0` for all projects (set in `Directory.Build.props`)
- **Package versions**: Centrally managed in `Directory.Packages.props` — add `<PackageVersion>` entries there, reference with `<PackageReference>` without `Version=` in `.csproj` files
- **Code style**: File-scoped namespaces, 4-space indent, nullable reference types enabled (enforced via `.editorconfig`)

## Architecture

### Communication Tiers

Data flows through three distinct protocols:

1. **Redis Pub/Sub** — Market data relay between Ingestor → Analyzer → Strategy. Channels defined in `src/Shared/Constants/RedisChannels.cs` (e.g., `price:BTCUSDT`, `signal:BTCUSDT`).
2. **gRPC (Protobuf)** — Typed service calls: Strategy → RiskGuard → OrderExecutor. Proto definitions live in `src/Shared/ProtoFiles/` and are compiled by the `Shared` project. All gRPC services require `AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)` for unencrypted HTTP/2.
3. **Redis Streams** — Durable audit log for all trade events (`trades:audit`). Published by Executor after order execution.

### Service Types

**BackgroundService workers** (`Host.CreateApplicationBuilder`): Ingestor, Analyzer, Strategy, Notifier, HistoricalCollector — these subscribe to channels and process events in `ExecuteAsync`.

**gRPC API servers** (`WebApplication.CreateBuilder`): RiskGuard (port 5013), Executor (port 5014) — expose gRPC endpoints plus an HTTP `/health` endpoint.

**Gateway.API**: YARP reverse proxy for dashboard routing (port 5000).

### Shared Library

`src/Shared/` is referenced by all services and contains:
- DTOs: `PriceTick`, `TradeSignal`, `OrderRequest`
- Generated gRPC clients/stubs from `.proto` files
- `RedisChannels` constants
- `SpanExtensions` for zero-allocation indicator calculations

### Data Persistence

- **PostgreSQL + TimescaleDB** (port 5433): `price_ticks` table uses yearly schema partitioning (e.g., `y2024.price_ticks`). Orders table in default schema. Access via Npgsql + Dapper — raw SQL with parameters, no ORM.
- **Connection string key**: `ConnectionStrings:Postgres` in `appsettings.json`
- **Redis key**: `Redis:Connection` in `appsettings.json`

### Observability

Executor.API has full OpenTelemetry integration with custom `OrderExecutionMetrics` class. Prometheus scrapes on `/metrics`. To extend tracing to other services, follow the pattern in `src/Services/Executor/Executor.API/Program.cs`.

### Key Environment Variables

Set in `infrastructure/.env` (copy from `.env.template`):
- `BINANCE_API_KEY`, `BINANCE_API_SECRET`, `BINANCE_USE_TESTNET`
- `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID`
- `PAPER_TRADING_MODE` — when `true`, Executor simulates fills without hitting Binance
- `MAX_DRAWDOWN_PERCENT`, `MIN_RISK_REWARD`, `MAX_POSITION_SIZE_PERCENT`, `COOLDOWN_SECONDS` — RiskGuard rule thresholds

### Database Migrations

Migration scripts are in `scripts/` (SQL files + PowerShell runners):
```powershell
./scripts/run-migration.ps1    # Apply schema partitioning migration
./scripts/run-rollback.ps1     # Rollback migration
```

### Adding a New Strategy

Implement `IStrategy` in `src/Services/Strategy/Strategy.Worker/Strategies/`, then register it in `Strategy.Worker/Program.cs`. Strategies receive `TradeSignal` from Redis and emit `OrderRequest` to RiskGuard via gRPC.

### Adding a New Risk Rule

Implement the rule interface in `src/Services/RiskGuard/RiskGuard.API/Rules/` and register it in `RiskGuard.API/Program.cs`. Rules run sequentially — any rejection short-circuits the chain.
