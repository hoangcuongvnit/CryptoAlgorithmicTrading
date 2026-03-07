# Plan 4 — Order Execution Engine (Paper-First) + Trade Audit Pipeline

## Objectives

- Deliver a production-structured **Order Executor gRPC service** that can execute validated orders with a safe default: **paper trading enabled**.
- Implement resilient exchange integration using **Polly retry + circuit breaker** to prevent cascading failures.
- Provide a durable, queryable **trade audit trail** using Redis Streams + PostgreSQL persistence.
- Complete Strategy → RiskGuard → Executor end-to-end flow for deterministic execution behavior.

## Scope for This Phase

- In scope:
	- `src/Services/Executor/Executor.API` implementation and hardening.
	- gRPC contract usage from `src/Shared/ProtoFiles/order_executor.proto`.
	- Paper mode simulation logic + controlled live mode switch.
	- Redis Streams audit publishing (`trades:audit`) and DB order persistence.
	- Integration validation with Strategy and RiskGuard.
- Out of scope:
	- Advanced order algorithms (TWAP/VWAP/iceberg).
	- Multi-exchange routing.
	- Portfolio-level netting and smart order routing.

## Current Baseline Assumptions

- Plan 2 data foundation is already running (historical + real-time ingestion to PostgreSQL).
- Gateway dashboard APIs already exist and can be extended for execution analytics later.
- Existing shared DTO/proto contracts are available in `src/Shared`.
- Default environment remains local Docker + PostgreSQL + Redis.

## Product Decisions

| Decision | Choice | Rationale |
|---|---|---|
| **Execution mode default** | Paper mode ON | Prevent accidental real-money trades during development |
| **Executor transport** | gRPC only | Consistent with low-latency internal architecture |
| **Exchange adapter** | Binance.Net wrapper (`BinanceOrderClient`) | Isolate third-party API logic and simplify testing |
| **Resilience policy** | Retry + Circuit Breaker (Polly) | Handle transient exchange/network failures safely |
| **Audit transport** | Redis Streams (`trades:audit`) | Durable event stream for replay/monitoring |
| **Source of truth for orders** | PostgreSQL `orders` table | Queryability, compliance, and historical analytics |

## Execution Architecture (Phase 4)

```text
Strategy.Worker
	 └─ gRPC PlaceOrder → Executor.API
												 ├─ Validate request invariants (basic sanity)
												 ├─ Read Trading:PaperTradingMode
												 ├─ IF paper mode:
												 │    └─ Simulate fill + create OrderResult
												 └─ ELSE live mode:
															└─ BinanceOrderClient.PlaceOrderAsync()
																	 wrapped by Polly pipeline
												 ├─ Persist order + result to PostgreSQL
												 ├─ Publish audit event to Redis Stream trades:audit
												 └─ Return PlaceOrderReply to caller
```

## Detailed Work Plan

### Step 1 — Harden gRPC Contract Boundary

- Verify request/response parity between proto and Shared DTO mapping.
- Add strict input validation in executor entry point:
	- required: `symbol`, `side`, `order_type`, `quantity`
	- numeric checks: `quantity > 0`, `price >= 0`, SL/TP coherence when provided
- Return structured failure responses (not exceptions) for business-invalid requests.

**Expected outcome:** malformed or dangerous requests are rejected deterministically before exchange interaction.

### Step 2 — Implement Paper Trading Core

- Add config flag:
	- `Trading:PaperTradingMode=true` by default.
- Implement deterministic paper fill simulator:
	- Market: fill at current reference price (with optional slippage basis points).
	- Limit: fill if market condition is met; otherwise mark pending/expired based on policy.
	- Stop/Stop-Limit: basic trigger simulation in this phase.
- Generate consistent `OrderResult` values (`order_id`, `filled_price`, `filled_qty`, timestamp, `is_paper=true`).

**Expected outcome:** full execution lifecycle works without external side effects.

### Step 3 — Implement Live Exchange Adapter + Resilience

- Implement/complete `Infrastructure/BinanceOrderClient.cs` with a single method:
	- `Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct)`
- Wrap live placement in Polly pipeline:
	- Retry: max 5 attempts, exponential backoff.
	- Circuit breaker: 50% failure ratio, 30s sampling, 60s break duration.
- Map Binance error payloads to normalized internal error codes/messages.
- Ensure API keys are loaded from env/config, never hardcoded.

**Expected outcome:** live mode is operational and protected against exchange instability.

### Step 4 — Order Persistence + Audit Stream

- Persist every execution attempt (paper/live) to PostgreSQL `orders` table with:
	- request payload fields
	- success/failure status
	- fill metadata
	- error message (if any)
	- `is_paper` flag
- Publish a compact audit event to Redis Stream `trades:audit` for downstream consumers.
- Include event version field for future backward-compatible schema changes.

**Expected outcome:** all orders are traceable both in DB and stream history.

### Step 5 — Integrate with Strategy and RiskGuard

- Ensure Strategy calls RiskGuard first, then Executor only on approval.
- Add idempotency guard at Strategy→Executor call boundary (client order token).
- Confirm rejection path:
	- RiskGuard rejection never reaches executor placement.
	- Rejected signals still emit observability event.

**Expected outcome:** no bypass path from strategy directly to exchange.

### Step 6 — Operational Safety Switches

- Add runtime controls in config:
	- `Trading:PaperTradingMode`
	- `Trading:GlobalKillSwitch`
	- `Trading:AllowedSymbols`
	- `Trading:MaxNotionalPerOrder`
- Enforce kill-switch and symbol allow-list in Executor before placement.
- Add startup warning log if live mode enabled on non-testnet.

**Expected outcome:** operators can halt or constrain execution quickly without redeploy.

## Data Contract and Storage Notes

## PostgreSQL `orders` minimum fields

- `id` (UUID, PK)
- `time` (TIMESTAMPTZ)
- `symbol` (TEXT)
- `side` (TEXT)
- `order_type` (TEXT)
- `quantity` (NUMERIC)
- `price` (NUMERIC, nullable)
- `filled_price` (NUMERIC, nullable)
- `filled_qty` (NUMERIC, nullable)
- `stop_loss` (NUMERIC, nullable)
- `take_profit` (NUMERIC, nullable)
- `strategy` (TEXT)
- `is_paper` (BOOLEAN)
- `success` (BOOLEAN)
- `error_msg` (TEXT, nullable)
- `client_order_id` (TEXT, nullable, unique where not null)

## Redis Stream `trades:audit` suggested fields

- `event_version`
- `order_id`
- `client_order_id`
- `symbol`
- `side`
- `order_type`
- `filled_price`
- `filled_qty`
- `is_paper`
- `success`
- `error_code`
- `time` (ISO-8601 UTC)

## API/Service-Level Validation Rules

- Reject `quantity <= 0`.
- Reject unsupported symbols (when allow-list is enabled).
- Reject if `GlobalKillSwitch=true`.
- For BUY: enforce `stop_loss < entry < take_profit` when both provided.
- For SELL: enforce `take_profit < entry < stop_loss` when both provided.
- Hard cap per-order notional by config to avoid fat-finger mistakes.

## Testing Strategy (Phase 4)

### Unit tests

- `PaperFillSimulatorTests`
	- market fill result
	- limit fill/not-fill behavior
	- deterministic timestamp/order id strategy (where applicable)
- `BinanceOrderClientTests` (mocked)
	- success mapping
	- known exchange failure mapping
	- retry + circuit breaker behavior under transient failures
- `OrderValidationTests`
	- invalid quantity, invalid SL/TP combinations, symbol blocklist checks

### Integration tests

- Strategy signal approved by RiskGuard reaches Executor and creates order record.
- RiskGuard rejection path produces no placement call.
- Executor success writes both DB row + Redis stream event.
- Executor failure still writes DB row + failure audit event.

## Delivery Checklist

- [ ] Executor gRPC `PlaceOrder` fully implemented.
- [ ] Paper mode is default and fully testable.
- [ ] Live mode wrapped with Polly retry + circuit breaker.
- [ ] PostgreSQL order persistence for both success and failure paths.
- [ ] Redis Streams `trades:audit` events emitted for all attempts.
- [ ] Kill switch, symbol allow-list, and max notional guardrails active.
- [ ] End-to-end integration test (Signal → RiskGuard → Executor) passing.
- [ ] Runbook updated with safe enablement steps for live trading.

## Suggested Timeline (4 Working Days)

- **Day 1:** gRPC boundary validation + paper simulator implementation.
- **Day 2:** live Binance adapter + resilience policies + error mapping.
- **Day 3:** DB persistence + Redis stream audit + integration wiring.
- **Day 4:** tests, operational guardrails, runbook, and staged verification.

## Definition of Done

1. Executor can process validated orders end-to-end in paper mode without external exchange dependency.
2. Live mode can be toggled safely and is resilient to transient exchange/API failures.
3. Every execution attempt produces an auditable DB record and stream event.
4. Risk checks cannot be bypassed in the normal Strategy flow.
5. Operational controls (kill switch, allow-list, notional cap) are enforceable at runtime.

## Risks and Mitigations

- **Risk:** duplicate orders caused by retries/timeouts.
	- **Mitigation:** enforce `client_order_id` idempotency and dedupe on persistence layer.
- **Risk:** exchange outage floods retries and destabilizes services.
	- **Mitigation:** bounded retries + circuit breaker + clear degraded-mode logging.
- **Risk:** accidental live trading in development.
	- **Mitigation:** paper mode default, explicit live-mode warning, and environment-based guards.
- **Risk:** inconsistent audit between DB and stream on partial failure.
	- **Mitigation:** persist-first strategy with retrying stream publish and reconciliation job in later phase.

---

Note: This plan intentionally prioritizes **safety, determinism, and observability** over advanced execution features. After this phase is stable, Phase 5 should focus on full tracing/metrics dashboards and stress testing under burst market conditions.
