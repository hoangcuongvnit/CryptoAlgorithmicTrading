## End-of-Session Trading Rule Change

## 1. Requested Business Change

Current behavior (today):
- Final 30 minutes of each session: block new positions and start closing open positions.

Target behavior (new):
- Final 30 minutes of each session: block new positions only.
- Final 2 minutes of each session: check open positions and close all remaining positions.

Scope note:
- Session length (currently 8h) is not changed in this request.
- Only end-of-session behavior is changed.

---

## 2. Impacted Services and Files

### A) Shared Session Model (used by Strategy, RiskGuard, Executor)

1) `src/Shared/Session/SessionSettings.cs`
- Why related:
	- Defines timing knobs used by all services.
- What to update:
	- Keep `LiquidationWindowMinutes = 30` as the no-new-position window.
	- Change `ForcedFlattenMinutes` from 10 to 2.
- Optional cleanup:
	- Current naming (`LiquidationWindowMinutes`) is misleading after the new rule because the 30-minute window is no longer forced closing.

2) `src/Shared/Session/SessionClock.cs`
- Why related:
	- Computes phase boundaries.
	- Currently: `LiquidationOnly` begins at session end -30m; `ForcedFlatten` begins at session end -10m.
- What to update:
	- With config change above, `ForcedFlatten` begins at session end -2m.
	- `LiquidationOnly` remains session end -30m to -2m.
- Behavior after change:
	- Phase timeline becomes: Open/SoftUnwind -> LiquidationOnly (no new entries) -> ForcedFlatten (close all).

3) `src/Shared/Session/SessionTradingPolicy.cs`
- Why related:
	- Defines gate helpers used by multiple services.
- What to review/update:
	- `IsReduceOnlyWindow(...)` currently returns true for both `LiquidationOnly` and `ForcedFlatten`.
	- New business intent suggests automatic closing should happen only in `ForcedFlatten`.
	- Consider adding a dedicated helper such as `IsAutoCloseWindow(...) => phase == ForcedFlatten`.

4) `src/Shared/DTOs/SessionPhase.cs`
- Why related:
	- Central phase enum consumed by multiple services.
- What to review:
	- Existing enum values are still usable.
	- No mandatory enum change unless you want clearer naming semantics.

---

### B) Executor Service (main closing logic)

5) `src/Services/Executor/Executor.API/Services/LiquidationOrchestrator.cs`
- Why related:
	- This service currently performs automatic closes during both `LiquidationOnly` and `ForcedFlatten`.
- Current conflicting logic:
	- `SessionPhase.LiquidationOnly` -> `DispatchCloseOrdersAsync(..., OrderType.Limit)`.
	- `SessionPhase.ForcedFlatten` -> `DispatchCloseOrdersAsync(..., OrderType.Market)`.
- Required update:
	- Stop auto-close in `LiquidationOnly`.
	- Keep auto-close only in `ForcedFlatten` (final 2 minutes).
	- Keep session-end emergency flatten fallback in `OnSessionEndAsync(...)`.
- Event/log updates needed:
	- `LiquidationStarted` messages should be revised to indicate "entry blocked window" rather than active liquidation.

6) `src/Services/Executor/Executor.API/Services/StartupReconciliationService.cs`
- Why related:
	- On startup, it currently closes recovered positions when in reduce-only window.
- Current conflicting logic:
	- Uses `_sessionPolicy.IsReduceOnlyWindow(session)`.
	- This can force close during the full final 30-minute window.
- Required update:
	- Reconciliation close-on-window should trigger only in `ForcedFlatten` (final 2 minutes), not entire final 30 minutes.
	- Keep stale cross-session emergency close behavior unchanged.

7) `src/Services/Executor/Executor.API/Services/SessionExitOnlyMonitorService.cs`
- Why related:
	- Controls session-based exit-only mode for new entries.
- Current behavior:
	- Activates exit-only when phase is `LiquidationOnly` or `ForcedFlatten`.
- Required update:
	- This behavior should remain (it matches "final 30 minutes no new positions").
	- Update comments/text to clarify this does not imply immediate auto-close.

8) `src/Services/Executor/Executor.API/Services/ShutdownOperationService.cs`
- Why related:
	- Holds user-facing reasons for session exit-only.
- Required update:
	- Keep final 30-minute session exit-only messaging.
	- Clarify wording so operators understand auto-close happens in final 2 minutes.

9) `src/Services/Executor/Executor.API/Program.cs`
- Why related:
	- Exposes status payload used by UI (`inFinal30Minutes`, `minutesToSessionEnd`).
- Required update:
	- Consider adding `inFinal2Minutes` field to status endpoint for better observability.
	- Keep `inFinal30Minutes` for entry-block window display.

10) `src/Services/Executor/Executor.API/appsettings.json`
- Required update:
	- `Trading:Session:ForcedFlattenMinutes = 2`.

---

### C) Strategy Service (entry gate)

11) `src/Services/Strategy/Strategy.Worker/Worker.cs`
- Why related:
	- Contains phase gate before sending orders.
- Current behavior:
	- Skips signals outside Open/SoftUnwind.
	- Therefore new entries are already blocked in final 30 minutes (`LiquidationOnly` and `ForcedFlatten`).
- Required update:
	- Mostly no logic change needed for the new rule.
	- Keep behavior aligned with RiskGuard (see conflict note below).

12) `src/Services/Strategy/Strategy.Worker/Services/SignalToOrderMapper.cs`
- Why related:
	- Has SoftUnwind-specific behavior (only strong signals).
- Required update:
	- No mandatory change for this request.
	- But review with RiskGuard policy to avoid phase inconsistencies.

13) `src/Services/Strategy/Strategy.Worker/appsettings.json`
- Required update:
	- `Trading:Session:ForcedFlattenMinutes = 2`.

---

### D) RiskGuard Service (final validation gate)

14) `src/Services/RiskGuard/RiskGuard.API/Rules/SessionWindowRule.cs`
- Why related:
	- Final gate for allowing new entries by current session phase.
- Required update:
	- Confirm the rule blocks new entries during final 30 minutes (this should stay).
	- Ensure message text matches new business language.

15) `src/Services/RiskGuard/RiskGuard.API/Rules/NoCrossSessionCarryRule.cs`
- Why related:
	- Prevents cross-session carry.
- Required update:
	- No direct logic change required.
	- Keep as guardrail for session-bound campaign.

16) `src/Services/RiskGuard/RiskGuard.API/appsettings.json`
- Required update:
	- `Trading:Session:ForcedFlattenMinutes = 2`.

---

### E) Frontend (status wording and operator understanding)

17) `frontend/src/pages/ShutdownControlPage.jsx`
- Why related:
	- Displays session exit-only reason and time to session end.
- Required update:
	- If backend adds `inFinal2Minutes`, show explicit warning: "Final 2-minute auto-close window active".
	- Keep 30-minute banner for no-new-entry policy.

18) `frontend/src/i18n/locales/en/shutdown.json`
19) `frontend/src/i18n/locales/vi/shutdown.json`
- Required update:
	- Adjust text to distinguish:
		- final 30 minutes: new entries blocked,
		- final 2 minutes: forced close of remaining open positions.

20) `frontend/src/i18n/locales/en/guidance.json`
21) `frontend/src/i18n/locales/vi/guidance.json`
- Required update:
	- Campaign rules currently say final 30 minutes are liquidation-only.
	- Change guidance to: final 30 minutes block new entries; forced close happens in final 2 minutes.

---

## 3. Important Existing Conflict to Resolve During This Change

Potential phase-gate inconsistency:
- Strategy worker allows signal flow in `SoftUnwind`.
- RiskGuard `SessionWindowRule` relies on `CanOpenNewPosition(...)`, which currently allows only `Open`.

Result:
- Some signals allowed by Strategy can still be rejected by RiskGuard.

Recommendation:
- Decide one canonical policy for allowed-entry phases and align Strategy + RiskGuard.

---

## 4. Implementation Task List (Recommended Order)

1) Update session timing config in all three services:
- Executor, Strategy, RiskGuard `appsettings.json`: set `ForcedFlattenMinutes = 2`.

2) Update Executor auto-close behavior:
- `LiquidationOrchestrator`: remove auto-close in `LiquidationOnly`; keep close-all in `ForcedFlatten` only.

3) Update startup recovery behavior:
- `StartupReconciliationService`: close recovered positions in final 2-minute window only (or stale-session), not whole final 30 minutes.

4) Keep entry block in final 30 minutes:
- Ensure SessionExitOnly monitor and session gates remain active across `LiquidationOnly + ForcedFlatten`.

5) Improve observability and operator UX:
- Add optional API/UI state for final 2-minute closing window.
- Update operator-facing messages in shutdown screen and guidance docs.

6) Align phase policy between Strategy and RiskGuard:
- Resolve `SoftUnwind` acceptance/rejection mismatch.

---

## 5. Validation Checklist

Functional checks:
- At T-30m to T-2m: new entries are rejected, existing open positions are NOT auto-closed.
- At T-2m to T: system actively closes all open positions.
- At session boundary: system is flat; if not flat, emergency flatten fallback still works.

Safety checks:
- Manual close-all still works regardless of session phase.
- Resume logic remains blocked during session-based exit-only as intended.

Observability checks:
- Logs/events clearly separate:
	- entry-block window start,
	- forced-close window start,
	- session-end flat/non-flat outcome.

UI checks:
- Shutdown page and guidance texts match the new 30m/2m behavior in both EN and VI.

