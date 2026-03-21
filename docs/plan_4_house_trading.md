# Plan — 4-Hour Session-Locked Trading Campaign

## Objectives

- Rebuild campaign logic so trading is strictly constrained to 4-hour sessions.
- Split each day into 6 independent sessions, operated as if by 6 separate employees.
- Guarantee all positions are closed before session end; no carry-over between sessions.
- Block new entries in the final 30 minutes of each session and focus only on liquidation.
- Optimize entry/exit timing under a hard intraday horizon while preserving risk controls.

## Business Rules (Must-Have)

1. A day is divided into six 4-hour sessions:

| Session | Time Window |
|---|---|
| S1 | 00:00 -> 04:00 |
| S2 | 04:00 -> 08:00 |
| S3 | 08:00 -> 12:00 |
| S4 | 12:00 -> 16:00 |
| S5 | 16:00 -> 20:00 |
| S6 | 20:00 -> 24:00 |

2. Every position opened in a session must be fully closed before that session ends.
3. No trade/order/position may survive across session boundaries.
4. In the last 30 minutes of a session, the system must not open new positions.
5. Last 30 minutes are dedicated to finding best available exits and flattening all exposure.

## Time Model and Session Identity

- Use a single configurable trading timezone (`Trading:SessionTimeZone`, default `UTC`).
- Compute immutable `SessionId` as: `yyyyMMdd-S{1..6}` in session timezone.
- Attach `SessionId` and `SessionEndUtc` to every signal/order/execution event.
- At runtime, all services should reason in UTC internally after session mapping.

## Target Architecture Changes

## New Components

1. `SessionClock` (Shared or Strategy layer)
- Calculates current session and phase (`OPEN_WINDOW`, `LIQUIDATION_WINDOW`, `SESSION_CLOSED`).
- Emits countdown metrics (`minutes_to_liquidation`, `minutes_to_end`).

2. `SessionTradingPolicy`
- Single source of truth for gate decisions:
	- CanOpenNewPosition?
	- MustForceClose?
	- IsCrossSessionOrderAllowed? (always false)

3. `PositionLifecycleManager`
- Tracks all open positions by `SessionId`.
- Provides real-time exposure status and closing progress.

4. `LiquidationOrchestrator`
- Activated automatically at `T_end - 30m`.
- Repeatedly computes best exits and dispatches close orders.
- Escalates aggressiveness as time approaches session end.

5. `SessionBoundaryGuard` (RiskGuard rule + Executor pre-check)
- Hard reject any order opening a new position during liquidation window.
- Hard reject any order referencing stale `SessionId`.

## Existing Services to Update

- `Strategy.Worker`
	- Add session-phase awareness before emitting `OrderRequest`.
	- Route generated signals into `entry` or `exit-only` mode.
- `RiskGuard.API`
	- Add `SessionWindowRule`, `NoCrossSessionCarryRule`.
- `Executor.API`
	- Enforce final hard stop if non-flat at session end.
	- Prioritize close orders over all other order intents in liquidation window.
- `Shared` DTO/proto
	- Include `session_id`, `session_phase`, `session_end_utc`.

## Trading Lifecycle Per Session

1. Session opens (`T0`)
- Enable entries and exits.
- Reset session-specific counters and state.

2. Normal trading (`T0` to `T_end - 30m`)
- New positions allowed if all strategy/risk criteria pass.
- Exit logic runs continuously.

3. Liquidation-only window (`T_end - 30m` to `T_end`)
- Block all new position opens.
- Keep only close/reduce-only actions.
- Increase close urgency over time.

4. End-of-session hard boundary (`T_end`)
- System must be flat (net position = 0 for all symbols).
- If any residual remains, execute emergency market close and alert.

## Optimal Entry/Exit Algorithm Design

## Design Principle

This is a finite-horizon problem with forced terminal liquidation. The algorithm should maximize expected value while accounting for time decay toward the hard cutoff.

## Entry Optimization (Only before last 30 minutes)

For each candidate signal at time $t$ in session:

$$
Score_{open}(t) = P_{win}(t) \cdot R_{target} - (1-P_{win}(t)) \cdot R_{risk} - Cost - \lambda \cdot \tau
$$

Where:
- $P_{win}(t)$: estimated probability of favorable move from model/features.
- $R_{target}$: expected gain if thesis succeeds.
- $R_{risk}$: expected loss under stop/invalid setup.
- $Cost$: fee + slippage estimate.
- $\tau$: minutes remaining to liquidation window.
- $\lambda$: time-decay penalty coefficient.

Open position only if:

$$
Score_{open}(t) > \theta_{open}
$$

and expected holding time fits remaining tradable window.

## Exit Optimization (All session, strongly in last 30 minutes)

Use finite-horizon optimal stopping approximation:

At each decision step, compare:
- `Close now` value: realized PnL minus immediate cost.
- `Hold 1 more step` value: expected PnL next step minus increased liquidation risk.

Approximate decision rule:

$$
Close\ if\ V_{close}(t) \geq E[V_{hold}(t+\Delta t)] - Penalty_{deadline}(t)
$$

Where `Penalty_deadline(t)` increases rapidly near session end.

## Practical Hybrid Strategy (Recommended)

1. `T_end - 45m` to `T_end - 30m` (soft unwind)
- Tighten trailing stops.
- Reduce take-profit distance.
- Avoid adding exposure except highest-conviction signals.

2. `T_end - 30m` to `T_end - 10m` (liquidation optimization)
- No new opens.
- Rank positions by close priority:
	- Highest unrealized loss first.
	- Highest volatility/illiquidity first.
	- Largest notional first.
- Use passive exits first when fill probability is acceptable.

3. `T_end - 10m` to `T_end` (forced flatten)
- Switch to aggressive close logic (IOC/market where needed).
- Re-check residual exposure every 15-30 seconds.
- End with guaranteed flat inventory.

This hybrid approach is robust, easy to operate, and near-optimal under strict deadline constraints.

## Order Priority Rules in Liquidation Window

- `reduce-only` close orders always take precedence.
- Any open/increase order is rejected with explicit reason code.
- If both close and stop-update are pending, execute close first.
- Cancel stale passive close orders before sending aggressive replacements.

## Data and Feature Requirements for Better Open/Close Points

- Micro-trend features: short EMA slope, momentum burst, breakout distance.
- Microstructure features: spread, order-book imbalance (if available), tick volatility.
- Execution features: recent slippage by symbol/session, fill latency, cancel ratio.
- Time features: minutes to liquidation and minutes to session end.

Store all features with outcome labels to enable offline calibration of $P_{win}$ and thresholds.

## Persistence and Audit Extensions

Add to `orders` and audit events:
- `session_id`
- `session_phase`
- `minutes_to_session_end`
- `is_reduce_only`
- `forced_liquidation` (bool)
- `liquidation_reason` (enum: `deadline`, `stop`, `target`, `manual`)

This allows post-session analysis of liquidation quality and missed opportunities.

## Risk and Safety Controls

1. `NoCrossSessionCarry` hard invariant
- If non-flat at `T_end`, trigger emergency close immediately.

2. `SessionOpenCooldown`
- Optionally block new entries in first 1-2 minutes after session start to avoid boundary noise.

3. `MaxOpenPositionsPerSession`
- Cap concurrent positions to improve final 30-minute liquidation reliability.

4. `LiquidityAwareClose`
- If market impact estimate exceeds threshold, start unwind earlier than 30 minutes.

## Implementation Plan

## Phase A — Foundation (1-2 days)

- Add shared session utilities (`SessionClock`, `SessionId` mapper).
- Add config:
	- `Trading:SessionHours=4`
	- `Trading:LiquidationWindowMinutes=30`
	- `Trading:SessionTimeZone=UTC`
- Extend DTO/proto contracts with session metadata.

## Phase B — Policy Enforcement (1-2 days)

- Implement `SessionTradingPolicy`.
- Add RiskGuard rules for open-block in last 30 minutes and stale session rejection.
- Add Executor hard checks for cross-session prevention.

## Phase C — Liquidation Engine (2-3 days)

- Build `LiquidationOrchestrator` with priority-based close queue.
- Implement aggressive escalation near deadline.
- Add emergency flatten fallback and alerting.

## Phase D — Optimization and Calibration (2-4 days)

- Add scoring-based entry gate with time-decay penalty.
- Add finite-horizon exit decision approximation.
- Backtest by session to tune $\lambda$, $\theta_{open}$, and close escalation thresholds.

## Testing Strategy

## Unit Tests

- Session mapping correctness at all boundary times.
- Open-order rejection in last 30 minutes.
- Stale `SessionId` rejection.
- Liquidation priority ordering correctness.
- Forced flatten trigger when residual exists at deadline.

## Integration Tests

- Full cycle: open in normal window -> close before session end.
- Verify no positions exist after each `T_end`.
- Verify opens are rejected in liquidation window.
- Verify emergency flatten path works when exchange fill is delayed.

## Backtest/Simulation Tests

- Compare baseline vs new campaign by session-level metrics:
	- Flat-at-close compliance rate (target: 100%)
	- Session Sharpe / PnL / max drawdown
	- Liquidation slippage in last 30 minutes
	- Missed-profit rate from early forced exits

## Observability and Alerts

Expose metrics:
- `session_open_positions`
- `minutes_to_session_end`
- `open_orders_blocked_total{reason="liquidation_window"}`
- `forced_liquidations_total`
- `session_flat_compliance_ratio`

Alerts:
- Critical: non-flat exposure at session end.
- Warning: liquidation window starts with too many open positions.
- Warning: repeated stale session order attempts.

## Definition of Done

1. System runs in strict 6-session/day mode with correct boundaries.
2. No position survives across sessions (enforced and verified).
3. No new opens in final 30 minutes.
4. All residual positions are force-closed by session end.
5. Entry/exit timing is improved by time-aware scoring and finite-horizon exit logic.
6. Dashboard/metrics clearly show session compliance and liquidation behavior.

## Recommended Next Step

Start with strict policy enforcement first (session guards + forced flatten), then optimize entry/exit scoring. Safety invariants should be non-negotiable before alpha optimization.
