# Bug Investigation: Recent Orders Always Failed

## Investigation Time
- Date: 2026-03-22
- Environment: Docker Compose (infrastructure/docker-compose.yml)

## Executive Summary
- No trading service containers were down or crashing.
- The failed orders were blocked by the system safety state: exit-only mode during/after close-all flow.
- This is an operational state coordination bug, not an infrastructure outage (Docker/Redis/Postgres were healthy).

## Verified Evidence
1. Container Health
- Command used: docker compose -f infrastructure/docker-compose.yml ps --all
- Result: core services were Up; no critical trading service was Exited.

2. Order-Level Failure Proof
- API checked: GET http://localhost:5094/api/trading/orders
- Repeated failed orders showed:
  - status = FAILED
  - success = false
  - errorMessage = "System is in exit-only mode. New position orders are blocked during the close-all operation."
- Failure window concentrated around 2026-03-22T02:51:05Z to 2026-03-22T02:54:05Z.

3. Daily Aggregate Confirmation
- API checked: GET http://localhost:5094/api/trading/report/daily
- Observed values:
  - totalTrades = 449
  - failedOrders = 20

4. Close-All Timeline Correlation
- API checked: GET http://localhost:5094/api/trading/control/close-all/history
- Found operation:
  - operationType = close_all_now
  - status = Completed
  - requestedAtUtc = 2026-03-22T02:50:42.109056Z
  - completedAtUtc = 2026-03-22T02:50:42.150785Z
- The failed order window happened immediately after this operation.

5. Current State at Investigation Time
- API checked: GET http://localhost:5094/api/trading/control/close-all/status
- Current values:
  - status = Idle
  - tradingMode = TradingEnabled
  - exitOnlyMode = false

## Root Cause Analysis
- During the close-all/exit-only period, Strategy continued to submit new entry requests.
- Executor correctly rejected these requests for safety.
- The system lacks a strict upstream gate to stop entry-order generation while trading mode is not enabled.

## Impact Assessment
- Severity: Medium.
- User impact: burst of FAILED orders and confusion that looks like system instability.
- System impact: no crash, but reduced trading quality and noisy failure data.

## Clear Fix Plan

### Goal
Prevent Strategy from sending new entry orders whenever the platform is in exit-only, shutdown, or recovery-lock states.

### Phase 1: Immediate Guardrails (1 day)
1. Add trading-mode pre-check in Strategy before sending orders to RiskGuard/Executor.
2. If mode is not TradingEnabled, skip order submission and log a structured reason.
3. Keep close/reduce-only flows allowed if business rules require them.

Expected outcome:
- No new FAILED entry orders caused by exit-only blocking.

### Phase 2: Cross-Service Contract Hardening (1-2 days)
1. Introduce a shared trading-state contract in Shared library (TradingEnabled, ExitOnly, Recovery, KillSwitch).
2. Expose one lightweight read endpoint or Redis state key as single source of truth.
3. Ensure Strategy, RiskGuard, and Executor use the same state names and behavior.

Expected outcome:
- Consistent behavior across services and no ambiguous state transitions.

### Phase 3: Observability and Alerting (1 day)
1. Add explicit counter metric: orders_rejected_exit_only_total.
2. Add structured logs on rejection with fields: symbol, side, mode, operationId, reason.
3. Add alert rule when rejection rate crosses threshold (for example, > 5 in 1 minute).

Expected outcome:
- Faster diagnosis and less dependence on noisy telemetry streams.

### Phase 4: UI/Operator Safety (optional, 0.5-1 day)
1. Show persistent ExitOnly status banner in dashboard.
2. Show last close-all operation id/time and current mode.
3. Disable manual entry actions in UI while not TradingEnabled.

Expected outcome:
- Operators immediately understand why entries are blocked.

## Test Plan
1. Unit tests
- Strategy does not publish/send entry orders when mode is ExitOnly.
- Strategy resumes normal order flow when mode returns to TradingEnabled.

2. Integration tests
- Trigger close-all and publish fresh signals.
- Verify zero entry calls reach Executor during ExitOnly.
- Verify no FAILED rows are created with exit-only message in that window.

3. Regression checks
- Normal trading still works in TradingEnabled mode.
- Close/reduce-only paths still execute correctly if enabled.

## Rollout Plan
1. Deploy to staging.
2. Run a controlled scenario: open positions -> close-all -> publish signals.
3. Validate metrics/logs and order table behavior.
4. Deploy to production in low-volume window.
5. Monitor rejection counters and daily failedOrders for 24 hours.

## Acceptance Criteria
1. Zero new entry-order failures caused by exit-only state.
2. Strategy creates no entry requests while trading mode is not TradingEnabled.
3. Rejection metrics and logs clearly identify operational blocking reasons.
4. Operators can confirm mode/state immediately from API or dashboard.

