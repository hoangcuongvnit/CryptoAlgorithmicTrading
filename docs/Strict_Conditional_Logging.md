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

**A. Mainnet Flow (Balances & Positions)**
1. Compare Balance: `IsDrifted = (Binance.AvailableBalance != Local.AvailableBalance)`
   * If `IsDrifted == true`: Update DB, Log to `state_drift_logs`, Send Telegram Alert.
2. Compare Positions (Loop through active symbols): `IsDrifted = (Binance.PositionAmt[symbol] != Local.PositionAmt[symbol])`
   * If `IsDrifted == true`: Update DB, Log to `state_drift_logs`, Send Telegram Alert.

**B. Testnet Flow (Positions ONLY)**
1. **IGNORE** Balance completely. Do not fetch, compare, or log balance data.
2. Compare Positions (Loop through active symbols): `IsDrifted = (Binance.PositionAmt[symbol] != Local.PositionAmt[symbol])`
   * If `IsDrifted == true`: Update DB, Log to `state_drift_logs`, Send Telegram Alert.

### 6.3. Crucial Notes for AI Coding Assistants
* **Decimal Precision Strictness:** When comparing Binance data and Local data in .NET, ensure all variables are strictly typed as `decimal`. Do not use `float` or `double` to prevent false positive drifts caused by floating-point precision loss (e.g., `0.10000000m == 0.1m` evaluates to true in `decimal`, which is correct for this comparison).
* **Memory Management:** Construct the Telegram alert string and the `state_drift_logs` entity ONLY inside the `if (IsDrifted)` block to save memory allocations during the 99% of times when the states are identical.
* **Batch Logging (Optional but Recommended):** If a single 5-minute tick detects multiple drifted symbols (e.g., 3 different coins drifted simultaneously), group them into a single Telegram message to avoid hitting Telegram API rate limits.