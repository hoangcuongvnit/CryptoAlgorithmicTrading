# Re-Enable Normal Trading After Close-All or Scheduled Pause

## 1. Objective

Design a safe and explicit **Resume Trading** capability so operators can restore normal entry trading when they decide not to keep the system paused, and no longer plan to shut down or restart.

This document extends the shutdown design in [docs/stop_all_trading.md](docs/stop_all_trading.md) and adds:

1. Operator-facing command to re-enable trading.
2. Guardrails to prevent unsafe resume while flatten is still active.
3. Clear status and audit trail for "paused" and "resumed" lifecycle.

---

## 2. Problem Analysis

From the current shutdown plan, the platform can:

1. Close all positions immediately.
2. Schedule a future close-all.
3. Block new entries using an exit-only gate.

Missing today:

1. No explicit command to return from `Exit-Only` / `Paused` to `TradingEnabled`.
2. No formal policy for when resume is allowed.
3. No resume-focused UI confirmation, notifications, or observability.

Operational risk without this feature:

1. Trading may remain unintentionally paused after operator cancels restart plans.
2. Manual/untracked unpause actions can reduce auditability.
3. Inconsistent service state between UI, Gateway, Executor, and RiskGuard.

---

## 3. Functional Requirements

## 3.1 New Core Action

1. `Resume Trading Now` (manual operator action).

## 3.2 Preconditions for Resume

Resume is accepted only when all checks pass:

1. No active close-all operation in `Requested`, `Scheduled`, or `Executing`.
2. No pending emergency flatten task.
3. System health checks pass for required services (Executor, RiskGuard, Redis, exchange connectivity policy).
4. Operator is authorized (admin role/key).

## 3.3 Resume Behaviors

1. Disable exit-only gate.
2. Transition trading mode to `TradingEnabled`.
3. Re-open normal order flow (non-reduce-only orders allowed).
4. Emit state transition and notification with timestamp and operator identity.

## 3.4 Idempotency and Concurrency

1. `Resume Trading` must require idempotency key.
2. Duplicate request with same key returns same result.
3. Concurrent resume requests should not produce multiple state flips.

---

## 4. Target State Model

Add explicit platform-level trading control state:

1. `TradingEnabled`
2. `ExitOnly`
3. `FlattenScheduled`
4. `FlattenExecuting`
5. `FlatReadyForShutdown`
6. `ResumePending`

Transition rules:

1. `TradingEnabled -> ExitOnly` when close-all is requested or schedule policy requires pre-block.
2. `ExitOnly -> FlattenExecuting` when close-all starts.
3. `FlattenExecuting -> FlatReadyForShutdown` when all positions are closed.
4. `ExitOnly -> TradingEnabled` when operator cancels scheduled close and confirms resume.
5. `FlatReadyForShutdown -> TradingEnabled` when operator confirms resume (without restart).
6. `ResumePending -> TradingEnabled` only after precondition checks succeed.

Rejected transitions:

1. Any resume attempt during `FlattenExecuting`.
2. Resume while a scheduled close is still active (unless canceled first).

---

## 5. API Design

## 5.1 Gateway Endpoints (new/extended)

1. `POST /api/control/trading/resume`
2. `GET /api/control/trading/mode`
3. `GET /api/control/trading/control-status`

Gateway behavior:

1. Proxy to Executor control endpoints.
2. Preserve `X-Correlation-Id`, `X-Idempotency-Key`, requester identity headers.

## 5.2 Executor Endpoints (new)

1. `POST /api/trading/control/resume`

Request example:

```json
{
  "reason": "cancel_shutdown_plan",
  "requestedBy": "admin",
  "confirmationToken": "...",
  "idempotencyKey": "..."
}
```

Response example:

```json
{
  "operationId": "c0b7f674-2f20-4670-b27f-fde8fe5bd8ce",
  "status": "Completed",
  "tradingMode": "TradingEnabled",
  "resumedAtUtc": "2026-03-21T11:10:00Z",
  "message": "Trading has been resumed successfully."
}
```

2. `GET /api/trading/control/status`

Response should include:

1. `tradingMode`
2. `activeCloseAllOperation`
3. `shutdownReady`
4. `resumeAllowed`
5. `resumeBlockReasons[]`
6. `lastControlAction`

---

## 6. Backend Design

## 6.1 New Services

1. `TradingModeService`
	- Single authority to read/write current trading mode.
	- Persists mode to DB and publishes change event.
2. `ResumeTradingService`
	- Validates resume preconditions.
	- Applies idempotency.
	- Performs atomic transition `ExitOnly/FlatReadyForShutdown -> TradingEnabled`.
3. `TradingControlReadinessService`
	- Computes `resumeAllowed` and block reasons.

## 6.2 Reuse Existing Components

1. Reuse recovery-aware RiskGuard behavior for gating consistency.
2. Reuse existing audit/event channel for control transitions.
3. Reuse system settings and timezone conversion infrastructure.

## 6.3 Safety Checks Before Final Resume Commit

1. Verify no `FlattenExecuting` task is running.
2. Verify no uncanceled schedule exists.
3. Verify control lock ownership (single-writer pattern).
4. Verify required dependencies are healthy (as configured).

If any check fails:

1. Reject with `409 Conflict` for state conflicts.
2. Reject with `422 Unprocessable Entity` for policy violations.
3. Return machine-readable `resumeBlockReasons`.

---

## 7. Data Model Updates

Extend `trading_control_operations` (or add companion table) to record resume actions.

Option A: same table with new operation type:

1. `operation_type`: add `resume_trading`
2. `status`: reuse lifecycle (`Requested`, `Executing`, `Completed`, `Failed`)

Option B: dedicated table `trading_mode_history`:

1. `id` (UUID PK)
2. `previous_mode`
3. `new_mode`
4. `reason`
5. `requested_by`
6. `requested_at_utc`
7. `completed_at_utc`
8. `idempotency_key` (unique)
9. `correlation_id`

Recommendation: use Option A for simpler querying of all control actions in one stream.

---

## 8. RiskGuard and Order Flow Implications

When in `ExitOnly`:

1. Block entry and position-increase orders.
2. Allow reduce-only and close orders.

When switched to `TradingEnabled`:

1. Remove exit-only restriction.
2. Keep normal risk rules unchanged (drawdown, position size, cooldown, etc.).
3. Ensure in-flight rejected entries during pause are not replayed automatically.

---

## 9. Frontend UX Design

Add a new action in Trading Shutdown Control panel:

1. `Resume Trading` primary action (enabled only when `resumeAllowed = true`).

Status UX:

1. Show current trading mode badge: `Trading Enabled` / `Exit-Only` / `Flatten Executing` / `Flat Ready`.
2. Show blocking reasons when resume is disabled.
3. Show last control action (who, when, reason).

Resume confirmation dialog:

1. Confirm text: "Resume normal trading and allow new entries?"
2. Display warnings if market is volatile or dependencies are degraded.
3. Optional typed confirmation (`RESUME TRADING`) for production live mode.

---

## 10. Notifications and Events

Emit notifications:

1. Resume requested.
2. Resume approved and completed.
3. Resume rejected with reasons.

Success message template:

1. Operation id
2. Previous mode -> new mode
3. Requested by
4. Timestamp

Example final text:

"Normal trading has been re-enabled. New entries are now allowed."

---

## 11. Security and Governance

1. Restrict resume endpoint to admin-level role/API key.
2. Require confirmation token and idempotency key.
3. Persist requester identity and source metadata.
4. Rate limit control endpoints to prevent abuse.
5. Keep all mode changes fully auditable.

---

## 12. Observability

Add metrics:

1. `trading_resume_requests_total`
2. `trading_resume_success_total`
3. `trading_resume_rejected_total`
4. `trading_mode_current` (gauge by mode)
5. `trading_resume_duration_seconds`

Add structured logs with:

1. `operation_id`
2. `correlation_id`
3. `previous_mode`
4. `new_mode`
5. `resume_block_reasons`

Alert examples:

1. Resume rejected repeatedly (> N times in M minutes).
2. Mode mismatch between Executor and RiskGuard.

---

## 13. Test Plan

Unit tests:

1. Valid and invalid resume transitions.
2. Resume idempotency behavior.
3. Block reason generation.
4. Concurrency race: two resume requests in parallel.

Integration tests:

1. Close-all completed -> resume successfully.
2. Scheduled close active -> resume rejected until canceled.
3. Cancel schedule -> resume succeeds.
4. Resume persistence survives service restart.
5. RiskGuard receives and enforces updated mode.

UAT scenarios:

1. Operator plans shutdown, then cancels and resumes normal trading.
2. Operator sees clear disabled reason when resume is unsafe.
3. Operator receives explicit success notification after resume.

---

## 14. Rollout Strategy

1. Phase 1: Backend mode model + resume endpoint + audit.
2. Phase 2: Frontend Resume Trading UX and status panel.
3. Phase 3: Observability, alerts, and hardening.
4. Phase 4: Live-mode stricter confirmation and operational runbook.

Feature flags:

1. `TradingResumeControlEnabled`
2. `TradingResumeStrictChecksEnabled`

---

## 15. Definition of Done

1. Operator can explicitly resume normal trading after close-all/scheduled pause workflows.
2. Resume is blocked with clear reasons when unsafe.
3. State transition is atomic, idempotent, and fully auditable.
4. UI clearly shows mode, readiness, and resume availability.
5. Notifications and metrics confirm successful re-enable operations.
