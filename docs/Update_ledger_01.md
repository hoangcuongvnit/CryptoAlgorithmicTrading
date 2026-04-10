# Update Ledger 01 - Unified Capital Source (MAINNET + TESTNET)

## Objective

- Use FinancialLedger as the single source of tradable capital for both MAINNET and TESTNET.
- Remove environment-specific capital logic from source code.
- Keep environment differences only at exchange API connectivity and order placement level.
- Keep Binance balance/holding synchronization for monitoring and reconciliation.
- For `Binance > local`: update local monitoring/snapshot data for UI only, do not mutate ledger funding.

## Scope

### In scope

- RiskGuard effective balance routing.
- Executor buy cash guard routing.
- Executor effective balance endpoint behavior.
- Documentation and rollout safety notes.

### Out of scope (this iteration)

- Removing `environment` field from gRPC/proto contracts.
- Full reconciliation policy rewrite for automatic ledger correction.
- Frontend wording cleanup for all pages.

## Phased Plan

### Phase 1 - Backend unification (capital source)

1. RiskGuard: route both environments to FinancialLedger for effective balance.
2. Executor BuyBudgetGuard: route both environments to FinancialLedger for buy-cash validation.
3. Executor `/api/trading/balance/effective`: resolve from FinancialLedger for both environments.

### Phase 2 - Reconciliation semantics alignment

1. Keep position synchronization and drift logging.
2. Keep `Binance > local` as observational update (snapshot/log/UI).
3. Keep `Binance < local` as review/alert policy unless feature flag enables corrective action.

### Phase 3 - UX and docs consistency

1. Remove testnet-only wording that implies different capital sources.
2. Clarify that exchange mode toggle is for Binance connectivity, not capital source.
3. Update architecture and runbook text.

## Current Implementation Status

### Completed in this change set

1. Updated `RiskGuard.API` `EffectiveBalanceProvider` to use FinancialLedger lookup for both MAINNET and TESTNET.
2. Updated `Executor.API` `BuyBudgetGuardService` to use FinancialLedger lookup for both MAINNET and TESTNET.
3. Updated `Executor.API` `/api/trading/balance/effective` to use FinancialLedger lookup for both MAINNET and TESTNET with shared fallback behavior.

### Completed in phase 2 (reconciliation semantics)

4. `RecoveryPolicyResolver`: direction-aware POSITION policy — `Binance > local` → `POSITION_EXCESS_OBSERVED` (observe only, no auto-correct); `Binance < local` → existing review/auto-correct policy.
5. `PeriodicReconciliationService`: passes signed drift delta (`BinanceValue - LocalValue`) to resolver instead of absolute value.

### Completed in phase 3 (UX + docs)

6. `OverviewPage.jsx`: replaced `isTestnetLedgerDisconnected` (testnet-only guard) with `isLedgerDisconnected` — warning shown for both environments when FinancialLedger is unavailable.
7. `frontend/src/i18n/locales/{vi,en}/overview.json`: replaced `testnetWarningTitle`/`testnetWarningBody` keys with `ledgerWarningTitle`/`ledgerWarningBody` and removed testnet-specific wording from both language variants.
8. `CLAUDE.md`, `README.md`, `.github/copilot-instructions.md`: updated effective balance routing documentation to reflect unified source (both environments → FinancialLedger).

### Pending

1. Add/adjust automated tests for unified routing behavior.

## Validation Checklist

1. In TESTNET mode, verify RiskGuard and Executor buy guard use FinancialLedger effective balance.
2. In MAINNET mode, verify the same behavior (no mainnet snapshot branch for capital gate).
3. Verify `/api/trading/balance/effective` returns FinancialLedger source path in both modes.
4. Verify fallback path remains explicit and marked stale.
5. Verify no regression in order execution flow and error code mapping.

## Risk Notes

1. FinancialLedger availability becomes more critical because both environments depend on it for capital gating.
2. Temporary fallback keeps service continuity but may diverge from intended strict capital control.
3. Consider moving to fail-closed policy after stabilization window.
