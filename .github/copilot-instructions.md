# CryptoAlgorithmicTrading Workspace Instructions

Use this repository as a live, production-oriented trading system. Prefer the current codebase and [CLAUDE.md](../CLAUDE.md) as the source of truth when behavior is unclear.

## Core Behavior

- Treat this repo as a .NET 10 monorepo with service boundaries that matter.
- Preserve current terminology: trading session and ledger session are not the same thing.
- Do not reintroduce paper trading concepts; the system is live-only.
- Keep changes aligned with the current service map, ports, and runtime flows.

## Build And Verification

- Use `dotnet build` for a full solution build.
- Use the specific project build command when changing one service.
- Use `dotnet test` or filtered tests for behavior changes that affect indicators, rules, reconciliation, or reporting.
- Validate infrastructure assumptions against Docker Compose when a change affects service-to-service communication.

## Repository Conventions

- Keep file-scoped namespaces and nullable enabled.
- Prefer Dapper and explicit SQL over introducing an ORM.
- Keep hot paths allocation-conscious.
- Respect the current shared DTOs, proto files, and Redis channel names instead of inventing new ones.
- Avoid broad refactors that rename core terms unless the change is required by the task.

## Service Map And Ports

- `RiskGuard.API`: gRPC 5013, HTTP 5093.
- `Executor.API`: gRPC 5014, HTTP 5094.
- `Notifier.Worker`: HTTP 5095.
- `TimelineLogger.Worker`: HTTP 5096.
- `FinancialLedger.Worker`: HTTP 5097, SignalR `/ledger-hub`.
- `Gateway.API`: HTTP 5000.

## Messaging And Contracts

- Redis Pub/Sub channels include `price:{SYMBOL}`, `signal:{SYMBOL}`, `coin:{SYMBOL}:log`, `executor:trading:mode`, `system:config:changed`, and `trading-engine:commands`.
- Redis Streams include `trades:audit` and `ledger:events`.
- Use the existing gRPC proto contracts for order validation and execution.
- Any gRPC client or server code must keep the HTTP/2 unencrypted support switch in place for local development.

## Session Rules

- Trading sessions are fixed 8-hour UTC cycles: S1 00:00-08:00, S2 08:00-16:00, S3 16:00-24:00.
- The final 30 minutes of a session are reserved for liquidation-oriented behavior.
- Ledger sessions are user-created reporting scopes and may span multiple days or months.
- When touching reporting, UI labels, or persistence, check whether the change belongs to trading-session logic or ledger-session logic.

## RiskGuard Rules

Keep the validation chain in the existing order unless a domain change explicitly requires a rewrite.

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

## Executor Safety Order

Before exchange placement, orders must still pass the current runtime checks in the established sequence:

- amount and notional limits
- spread and optional consensus checks
- sell-side local quantity guard
- buy-side cash or budget guard
- exchange placement

## Effective Balance Routing

- Both MAINNET and TESTNET resolve effective balance from FinancialLedger at `/api/ledger/balance/effective`.
- `BINANCE_USE_TESTNET` controls Executor order routing (Binance API connectivity) only — it does not change the capital source.
- Keep `Risk:AllowVirtualBalanceFallback` as a compatibility fallback path when FinancialLedger is unreachable.
- When editing Docker Compose or container config, use service DNS names, not `localhost`, for inter-service requests.

## Container And Environment Rules

- `Executor__BaseUrl` should point to `http://trading_executor:5094` in Docker Compose.
- `FinancialLedger__BaseUrl` should point to `http://trading_financialledger:5097` in Docker Compose.
- `BINANCE_USE_TESTNET` controls Executor trading mode only.
- Ingestor remains live market-data only.

## Documentation Rules

- Keep [README.md](../README.md) public-facing and concise.
- Keep [ARCHITECTURE.md](../ARCHITECTURE.md) technical and design-focused.
- Use this file for prescriptive repo-specific guidance that should shape AI code generation.

## Good Defaults For Changes

- Prefer the smallest focused change that fixes the root cause.
- Do not update unrelated services or files unless the task depends on them.
- Add or adjust tests when the change alters behavior.
- Verify the result against current repo facts instead of assuming historical documentation is correct.
