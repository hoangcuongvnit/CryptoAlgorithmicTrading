# Stop All Trading and Flatten Positions - Detailed Implementation Plan

## 1. Goal

Build a safe operational feature that lets the operator:

1. Close all currently open positions with one confirmed action.
2. Optionally schedule this action (for example: in 5 minutes).
3. Block new entries during shutdown preparation.
4. Receive a clear completion signal: all positions are flat, so the operator can safely shut down or restart the server.

This feature should work for both immediate shutdown and planned shutdown/restart windows.

---

## 2. Current Baseline (Already in the Codebase)

The system already includes important building blocks we should reuse:

1. Recovery and state gating in Executor (`Booting -> RecoveryMode -> RecoveryExecuting -> RecoveryVerified -> TradingEnabled`).
2. Recovery-state-aware risk rule (`RecoveryWindowRule`) in RiskGuard to block non-reduce-only entries before trading is fully enabled.
3. Session liquidation and emergency flatten logic (`LiquidationOrchestrator`) in Executor.
4. Gateway API + frontend settings page infrastructure.

Gap today:

1. No operator-facing command for "Close all now".
2. No scheduler for future flatten time.
3. No explicit shutdown-readiness status and acknowledgement flow.
4. System settings currently handle timezone only.

---

## 3. Functional Requirements

## 3.1 Core Actions

1. `Close All Now`
2. `Schedule Close All` at absolute time or relative delay (for example: +5 minutes)
3. `Cancel Scheduled Close` (only before execution starts)

## 3.2 Safety and Confirmation

1. Require explicit confirmation dialog before execution.
2. Display affected positions count and symbols before confirming.
3. Enter `Exit-Only` mode immediately after confirmation:
	- Block new position opens/increases.
	- Allow reduce-only exits.
4. Prevent duplicate overlapping commands (idempotency key + single active operation).

## 3.3 Completion and Notification

1. Publish status transitions:
	- `Requested`
	- `Scheduled`
	- `Executing`
	- `Completed`
	- `CompletedWithErrors`
	- `Canceled`
2. Notify operator when flatten is complete with a clear message:
	- "All open positions are closed. It is safe to shutdown or restart now."
3. Expose operation status in dashboard and system events.

---

## 4. Proposed Architecture

## 4.1 New Domain Component: Trading Shutdown Controller

Add a controller/orchestrator inside Executor.API (or a dedicated shared worker if desired) that:

1. Owns the lifecycle of flatten operations.
2. Calls existing execution pipeline with reduce-only close orders.
3. Reuses position sources from `PositionTracker` and Binance reconciliation where needed.
4. Writes operation state to Redis + PostgreSQL audit tables.

Recommended internal services:

1. `ShutdownOperationService` (state machine + persistence + idempotency)
2. `CloseAllSchedulerService` (timer/scheduled dispatch)
3. `CloseAllExecutorService` (actual flatten orchestration)

## 4.2 Control Plane Routing

1. Frontend sends commands to Gateway API.
2. Gateway forwards to Executor REST endpoints.
3. Executor runs operation and emits progress events.
4. Gateway/Frontend polls or streams operation status.

---

## 5. API Design

## 5.1 Gateway Endpoints (new)

1. `POST /api/control/close-all`
2. `POST /api/control/close-all/schedule`
3. `POST /api/control/close-all/cancel`
4. `GET /api/control/close-all/status`
5. `GET /api/control/close-all/history?limit=...`

Gateway should proxy these to Executor and preserve correlation headers.

## 5.2 Executor Endpoints (new)

1. `POST /api/trading/control/close-all`

Request example:

```json
{
  "reason": "server_shutdown",
  "requestedBy": "admin",
  "confirmationToken": "...",
  "idempotencyKey": "..."
}
```

2. `POST /api/trading/control/close-all/schedule`

Request example:

```json
{
  "executeAtUtc": "2026-03-21T10:35:00Z",
  "reason": "planned_restart",
  "requestedBy": "admin",
  "confirmationToken": "...",
  "idempotencyKey": "..."
}
```

3. `POST /api/trading/control/close-all/cancel`
4. `GET /api/trading/control/close-all/status`

Response should include:

1. Current operation id
2. Status
3. Scheduled time
4. Open positions remaining
5. Last update timestamp
6. Shutdown ready flag (`shutdownReady: true/false`)

---

## 6. State Machine

Define an explicit operation state machine:

1. `Idle`
2. `Requested`
3. `Scheduled`
4. `Executing`
5. `Completed`
6. `CompletedWithErrors`
7. `Canceled`

Rules:

1. Only one active operation (`Requested`, `Scheduled`, `Executing`) at a time.
2. While active operation exists, reject new schedule/close-all commands unless same idempotency key.
3. `shutdownReady = true` only when:
	- No open positions.
	- No pending close tasks.
	- Operation is `Completed`.

---

## 7. Execution Logic

## 7.1 Immediate Close-All Flow

1. Validate confirmation token and idempotency key.
2. Set global trading gate to exit-only mode.
3. Snapshot open positions.
4. For each symbol, issue reduce-only close order via existing `OrderExecutionService`.
5. Retry with bounded policy on transient exchange errors.
6. Re-check open positions after each pass.
7. If still non-flat near timeout, escalate to emergency market close.
8. Final verification with local tracker (+ optional exchange check in live mode).
9. Mark operation complete and emit completion notification.

## 7.2 Scheduled Close-All Flow

1. Accept schedule request and validate `executeAtUtc > now + minLeadTime`.
2. Store schedule in persistent state (DB) and in-memory timer.
3. Immediately enforce entry blocking policy (optional strict mode) or enable pre-block window (configurable).
4. At trigger time, execute the same flow as immediate close-all.
5. If service restarts before trigger, reload and re-arm schedules on startup.

---

## 8. Data Model Changes

Add a dedicated table `trading_control_operations`:

1. `operation_id` (UUID PK)
2. `operation_type` (`close_all_now`, `close_all_scheduled`)
3. `status`
4. `requested_by`
5. `reason`
6. `requested_at_utc`
7. `scheduled_for_utc` (nullable)
8. `started_at_utc` (nullable)
9. `completed_at_utc` (nullable)
10. `shutdown_ready` (bool)
11. `result_summary` (jsonb)
12. `error_summary` (jsonb)
13. `idempotency_key` (unique)
14. `correlation_id`

Add event/audit entries for each transition in `trades:audit` or system events stream.

---

## 9. Settings and Configuration

Extend system settings with these keys:

1. `trading.close_all.enabled` (bool)
2. `trading.close_all.min_lead_seconds` (int)
3. `trading.close_all.execution_timeout_seconds` (int)
4. `trading.close_all.retry_max_attempts` (int)
5. `trading.close_all.allow_schedule` (bool)
6. `trading.close_all.pre_block_minutes` (int)

Keep timezone support as source for operator input conversion to UTC.

---

## 10. Frontend UX Plan (Settings Page)

Add a new section in system settings: `Trading Shutdown Control`.

## 10.1 UI Elements

1. `Close All Now` danger button.
2. `Schedule Close All` controls:
	- Relative delay quick options: `+5m`, `+10m`, `+15m`
	- Absolute datetime picker (timezone-aware)
3. `Cancel Schedule` button when a job is pending.
4. Status panel showing:
	- Current state
	- Countdown to execution
	- Open positions remaining
	- Last error/success message
	- `Shutdown Ready` badge

## 10.2 Confirmation Dialog

Confirmation dialog must include:

1. Current number of open positions and symbols.
2. Warning that new entries will be blocked.
3. Optional typed confirmation phrase (`CLOSE ALL`).
4. Optional checkbox: "I understand this action may force market exits." 

---

## 11. Notification Plan

Send notifications through existing Notifier channel (and UI events):

1. On request accepted.
2. On schedule created/canceled.
3. On execution start.
4. On completion success.
5. On completion with errors.

Success template:

1. Operation id
2. Closed symbols count
3. Duration
4. Final statement: safe to shutdown/restart now.

---

## 12. Security and Access Control

1. Restrict control endpoints to admin role/API key.
2. Require anti-replay/idempotency key for write operations.
3. Log requester identity and source IP.
4. Apply rate limit for control endpoints.

Important: by default, do not execute OS shutdown/restart directly from web API. This feature should primarily prepare trading state and notify readiness. If OS control is later required, implement through a secured local agent with strict allowlist.

---

## 13. Observability

Add metrics:

1. `close_all_requests_total`
2. `close_all_scheduled_total`
3. `close_all_executions_total`
4. `close_all_failures_total`
5. `close_all_duration_seconds`
6. `close_all_open_positions_remaining`

Add structured logs by `operation_id` and `correlation_id`.

Add alerts:

1. Close-all execution timed out.
2. Close-all completed with residual open positions.
3. Scheduled execution missed trigger time.

---

## 14. Test Plan

## 14.1 Unit Tests

1. State transitions and invalid transition rejection.
2. Idempotency behavior.
3. Schedule validation and timezone conversion.
4. Exit-only gate behavior while operation active.

## 14.2 Integration Tests

1. Immediate close-all in live mode with multiple symbols.
2. Scheduled +5 minutes close-all execution.
3. Cancel schedule before execution.
4. Service restart while schedule pending (must recover and execute).
5. Partial exchange failure and retry/escalation path.

## 14.3 UAT Scenarios

1. Operator schedules close-all in 5 minutes, receives completion, then safely restarts server.
2. Operator executes immediate close-all during volatile market; no new entries are created.
3. Operator sees clear status updates in dashboard without manual refresh confusion.

---

## 15. Rollout Plan

1. Phase A: Backend API + state machine + live mode deployment.
2. Phase B: Frontend controls + confirmation UX + notifications.
3. Phase C: Live mode guarded rollout (symbol whitelist).
4. Phase D: Hardening (alerts, timeout tuning, failover drills).

Feature flags:

1. `CloseAllControlEnabled`
2. `CloseAllSchedulingEnabled`
3. `CloseAllLiveModeEnabled`

---

## 16. Definition of Done

1. Operator can close all open positions with one confirmed action.
2. Operator can schedule close-all at a future time (including +5 minutes).
3. System blocks new entries during close-all operation.
4. System emits explicit completion and shutdown-readiness notification.
5. Operation is fully auditable and recoverable after restart.
6. Integration and UAT scenarios pass with live trading safeguards.
