## 6. Strict Conditional Logging & Alerting (Anti-Spam Mechanism)

To prevent database bloat and alert fatigue on Telegram, the `ReconciliationWorker` must enforce a **Strict Delta-Only Update Rule**. The system must explicitly ignore identical data states.

### 6.1. The "Diffing" Logic Framework
During every 5-minute reconciliation cycle, the worker calculates the delta between Binance Truth ($B$) and Local State ($L$). 

**The Rule:**
* **IF $B == L$:** The state is perfectly synchronized. **DO NOTHING.** (No DB updates, no inserts into `state_drift_logs`, no Telegram alerts. Silently proceed to the next symbol).
* **IF $B \neq L$:** State drift is detected. **EXECUTE RECOVERY:**
  1. `UPDATE` local state tables.
  2. `INSERT` into `state_drift_logs`.
  3. Dispatch Telegram alert.

### 6.2. Detailed Execution Flow (Testnet vs. Mainnet)

**A. Mainnet Flow (Balances & Spot Holdings)**
1. Compare Balance: `IsDrifted = (Binance.AvailableBalance != Local.AvailableBalance)`
   * If `IsDrifted == true`: Update DB, Log to `state_drift_logs`, Send Telegram Alert.
2. Compare Spot holdings (Loop through active symbols): `IsDrifted = (Binance.Free+Locked[symbol] != Local.NetQty[symbol])`
   * If `IsDrifted == true`: Update DB, Log to `state_drift_logs`, Send Telegram Alert.

**B. Testnet Flow (Spot Holdings ONLY)**
1. **IGNORE** Balance completely. Do not fetch, compare, or log balance data.
2. Compare Spot holdings (Loop through active symbols): `IsDrifted = (Binance.Free+Locked[symbol] != Local.NetQty[symbol])`
   * If `IsDrifted == true`: Update DB, Log to `state_drift_logs`, Send Telegram Alert.

### 6.3. Crucial Notes for AI Coding Assistants
* **Decimal Precision Strictness:** When comparing Binance data and Local data in .NET, ensure all variables are strictly typed as `decimal`. Do not use `float` or `double` to prevent false positive drifts caused by floating-point precision loss (e.g., `0.10000000m == 0.1m` evaluates to true in `decimal`, which is correct for this comparison).
* **Memory Management:** Construct the Telegram alert string and the `state_drift_logs` entity ONLY inside the `if (IsDrifted)` block to save memory allocations during the 99% of times when the states are identical.
* **Batch Logging (Optional but Recommended):** If a single 5-minute tick detects multiple drifted symbols (e.g., 3 different coins drifted simultaneously), group them into a single Telegram message to avoid hitting Telegram API rate limits.

### 6.4. Implementation Status (Spot-Only)
Current implementation is **Spot-only** and compares Binance Spot balances against local net quantities.

**Implemented**
* Periodic reconciliation loop in Executor with delta-only behavior.
* Spot account snapshot comparison against local execution state.
* Mainnet balance drift logging against virtual budget.
* Testnet skips balance drift comparison.
* `state_drift_logs` persistence for each reconciliation cycle.
* Notifier-friendly `ReconciliationDrift` event for batch delivery.
* Recovery policy support for `DetectOnly`, `AutoCorrect`, and `RequireApproval`.
* Session boundary protection for auto-correction.
* Local pre-check sell guard in Executor using `PositionTracker` only, without a fresh Binance call.
* Pre-buy guard in Executor uses `BudgetRepository` on Testnet and reconciled cash snapshot on Mainnet.
* Reconciliation health endpoint.
* Latest drift report endpoint.

**Current drift report endpoint**
* `GET /api/trading/reconciliation/latest`
* Returns the most recent reconciliation cycle from `state_drift_logs`, including counts and per-drift details.

**Still configurable**
* `Trading:Reconciliation:Enabled` defaults to `false`.
* `Trading:Reconciliation:RecoveryMode` defaults to `DetectOnly`.
* `Trading:Reconciliation:BalancePolicy` defaults to `LogOnly`.

**Not in scope**
* Derivatives reconciliation.
* Paper trading reconciliation.
* Auto-mirroring balance drift into exchange state.