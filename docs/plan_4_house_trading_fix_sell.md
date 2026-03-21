# Plan Addendum — Crash Recovery and Session Continuity for 4-Hour Trading Sessions

## Purpose

This document extends the existing session-locked campaign plan in [docs/plan_4_house_trading.md](docs/plan_4_house_trading.md) with hard requirements for unexpected shutdowns and restarts.

Goal: if the system crashes while positions are open, restart safely, reconcile state with Binance, finish all required close actions inside the 4-hour session model, and only then allow normal trading progression.

## Incident Scenario Covered

Example:
- Session S1 runs from `00:00` to `04:00` (session timezone).
- The system crashes while orders/positions are still active.
- On restart, the platform must inspect historical local events and Binance live state.
- Any still-open exposure must be handled using risk checks and session liquidation rules.
- If restart happens before that session ends, trading can continue in the same session after recovery checks pass.

## New Must-Have Requirements

1. **Crash-safe restart gate**
- On startup, all trading services must enter `RECOVERY_MODE`.
- No new open/increase orders are allowed until reconciliation is complete.

2. **Exchange truth reconciliation**
- System must compare local state (orders, fills, positions, audit log) with Binance state (open orders, position quantities, balances, recent fills).
- Binance state is treated as source of truth when conflicts are detected.

3. **Session-aware recovery**
- Startup logic must identify the active/last relevant `SessionId` using `Trading:SessionTimeZone`.
- Every recovered position/order must be tagged with `SessionId`, `SessionPhase`, and `SessionEndUtc`.

4. **No cross-session carry invariant remains strict**
- If residual exposure belongs to an already-ended session, force emergency close immediately.
- New session opening is blocked until all legacy exposure is flat.

5. **In-session continuation**
- If restart occurs within the same 4-hour session and reconciliation succeeds:
	- resume normal strategy flow only if policy gates allow;
	- continue liquidation behavior if currently in the final 30-minute window.

6. **Risk re-evaluation before any close execution**
- Before sending recovery close orders, re-run RiskGuard rules (size, drawdown, liquidity, cooldown, etc.).
- During liquidation/deadline pressure, only reduce-only actions are permitted.

## Recovery State Machine

Define startup lifecycle:

1. `BOOTING`
- Services start, load config, connect to Redis/Postgres/Binance.

2. `RECOVERY_MODE`
- Freeze new entries.
- Load last known local session state and outstanding intents.
- Pull Binance actual open orders and positions.
- Reconcile and build `RecoveryActionPlan`.

3. `RECOVERY_EXECUTING`
- Cancel stale/conflicting opens.
- Submit reduce-only closes when needed.
- Escalate to aggressive close near session end.

4. `RECOVERY_VERIFIED`
- Confirm local state and Binance state are consistent.
- Confirm policy invariants are satisfied.

5. `TRADING_ENABLED`
- Resume strategy execution if current session window allows opens.
- Otherwise stay in exit-only mode until session boundary.

## Startup Decision Logic

At startup time `t_now`:

1. Determine `currentSession` and `minutesToEnd`.
2. Fetch unresolved local trade graph for recent sessions (`N` configurable, default 2 sessions lookback).
3. Fetch Binance open orders/positions/fills.
4. Reconcile by symbol:
- local open + exchange flat -> mark locally closed with reconciliation reason.
- local closed + exchange open -> create recovery close intent.
- both open but quantities differ -> correct quantity via reduce-only delta orders.

5. Apply session policy:
- If recovered exposure maps to ended session: emergency flatten now.
- If in active session and `minutesToEnd > LiquidationWindowMinutes`: may return to normal mode after flatness checks.
- If in active session and `minutesToEnd <= LiquidationWindowMinutes`: keep exit-only mode.

6. Verify global flatness rules before unlocking next session.

## Service-Level Changes

## Shared

- Add recovery metadata fields to DTO/audit model:
	- `recovery_run_id`
	- `recovery_source` (`startup_reconcile`, `manual_reconcile`)
	- `reconciliation_status` (`matched`, `corrected`, `forced_close`)
	- `original_session_id`

- Add `RecoverySnapshot` contract for cross-service handoff.

## Strategy.Worker

- Start in paused state until `TRADING_ENABLED` event.
- If restart is within same active session and policy permits, reload model context and continue signal generation.
- If in liquidation window, emit close-only recommendations.

## RiskGuard.API

- Add `RecoveryWindowRule`:
	- block all open/increase orders during `RECOVERY_MODE`.
	- allow only reduce-only requests tied to recovery plan.

- Add `RecoveredSessionConsistencyRule`:
	- reject requests missing required recovered session metadata.

## Executor.API

- Add `StartupReconciliationService`:
	- queries Binance and local store;
	- creates deterministic action plan;
	- executes cancel/close sequence by priority.

- Enforce `reduce-only` on all recovery closes.
- Implement deadline escalation:
	- passive close early;
	- IOC/market close near `SessionEndUtc`.

## HouseKeeper / HistoricalCollector

- Persist recovery incident timeline and outcome summary for postmortem analysis.

## Data Persistence Additions

Extend order/audit/reporting tables with:
- `recovery_run_id` (nullable UUID)
- `is_recovery_action` (bool)
- `recovery_action_type` (`cancel_stale_open`, `reduce_close`, `forced_flatten`)
- `recovery_detected_at_utc`
- `recovery_completed_at_utc`
- `exchange_position_qty_before`
- `exchange_position_qty_after`

## Observability and Alerting

New metrics:
- `recovery_runs_total`
- `recovery_duration_seconds`
- `recovery_mismatches_total`
- `recovery_forced_flatten_total`
- `recovery_resume_success_total`
- `startup_blocked_new_orders_total`

New alerts:
- Critical: recovery failed to flatten before session end.
- Critical: unresolved exchange/local mismatch after max retries.
- Warning: restart occurred in liquidation window with high residual notional.

## Testing Strategy (Recovery Focus)

## Unit Tests

- Session classification during restart at boundary times.
- Reconciliation matcher for all mismatch combinations.
- Policy gate correctness in `RECOVERY_MODE`.
- Transition rules between recovery states.

## Integration Tests

- Crash mid-session with open positions -> restart -> reconcile -> continue same session.
- Crash near deadline (`T_end - 10m`) -> aggressive liquidation -> flat by `T_end`.
- Crash spanning session boundary -> no new opens until legacy exposure is closed.
- Exchange/local disagreement -> corrected state persisted and auditable.

## Chaos/Resilience Tests

- Kill Strategy/Executor during active trading repeatedly.
- Simulate delayed Binance API and partial fill responses.
- Validate idempotent recovery when startup reconcile is triggered multiple times.

## Rollout Plan

1. Implement startup freeze + reconciliation read-only dry run.
2. Enable automated reduce-only recovery closes in paper mode.
3. Enable production recovery with guarded rollout by symbol whitelist.
4. Enable full auto-resume after verified reconciliation.

## Definition of Done (Extended)

1. Unexpected shutdowns do not cause uncontrolled carry-over exposure.
2. Restart flow always reconciles with Binance before accepting new entries.
3. System can resume same-session trading after successful in-session recovery.
4. System enforces close-out of pre-existing exposure before any new session trading.
5. All recovery actions are traceable via metrics, logs, and audit fields.
