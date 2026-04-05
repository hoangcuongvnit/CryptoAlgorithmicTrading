# 4-Hour Session Trading Report: Requirements and Implementation Specification

## 1) Goal and Scope

This document defines the reporting requirements for the new 4-hour session-locked trading campaign.

The system must produce reports per session, with exactly 6 sessions per day:

- S1: 00:00 -> 04:00
- S2: 04:00 -> 08:00
- S3: 08:00 -> 12:00
- S4: 12:00 -> 16:00
- S5: 16:00 -> 20:00
- S6: 20:00 -> 24:00

All sessions are computed in `Trading:SessionTimeZone` (default `UTC`).

The report must allow users to clearly understand, for each session:

1. Opening balance (start of session).
2. Closing balance (end of session).
3. Profit/Loss (PnL) of the session.
4. Number of trades executed in the session.
5. Which coins were traded in the session.
6. How total account value changes across sessions.

The reporting model applies to live trading operations.

---

## 2) Session Model and Boundaries

## A. Session Identity

- Each event is tagged with immutable `SessionId` in format `yyyyMMdd-S{1..6}`.
- Session mapping is derived from event timestamp in session timezone.
- Runtime services may use UTC internally, but report grouping must follow session timezone.

## B. Hard Boundary Rules

- No position may carry over to the next session.
- Last 30 minutes are liquidation-only (no new open positions).
- Report validation must confirm flat inventory at each session end.

## C. Late Data Handling

- If an execution arrives late but belongs to an earlier session by timestamp, it must still be recorded in the correct historical session.
- Report rows are append-safe and revision-safe (support recalculation and versioning if needed).

---

## 3) Required Report Sections

## A. Daily Session Overview (Top Summary)

For selected day, show:

- Total sessions: 6
- Sessions completed successfully (flat-at-close)
- Total daily realized PnL
- Total daily fees
- Net daily PnL
- Best session (max net PnL)
- Worst session (min net PnL)

## B. Session Financial Table (Core Requirement)

Table: `Session Financial Summary`

Required columns:

- Date
- SessionId
- Session Start Time
- Session End Time
- Opening Cash Balance
- Opening Holdings Market Value
- Opening Total Equity
- Closing Cash Balance
- Closing Holdings Market Value
- Closing Total Equity
- Session Realized PnL
- Session Unrealized PnL at close (normally near 0 if flat)
- Session Fees
- Session Net PnL
- Equity Change %
- Flat-at-close status (`true`/`false`)
- Last Updated At

## C. Session Trading Activity

Table: `Session Trading Activity`

Required columns:

- Date
- SessionId
- Total Orders Submitted
- Total Orders Filled
- Buy Count
- Sell Count
- Rejected Count
- Cancelled Count
- Distinct Symbols Traded
- Symbols List (e.g., `BTCUSDT, ETHUSDT`)
- Most Active Symbol
- Total Filled Quantity (base unit)

## D. Session Coin Breakdown

Table: `Session Symbol Breakdown`

Required columns:

- Date
- SessionId
- Symbol
- Buy Count
- Sell Count
- Buy Quantity
- Sell Quantity
- Avg Buy Price
- Avg Sell Price
- Realized PnL
- Fees
- Net PnL
- Win Trades
- Loss Trades

## E. Equity Change Chart by Session (Mandatory)

Primary chart required:

- X-axis: `SessionId` sequence (S1 -> S6) for selected date or date range.
- Y-axis: `Closing Total Equity`.
- Series 1: Closing total equity by session.
- Series 2 (optional): Opening total equity by session.
- Series 3 (optional): Session net PnL bars.

Behavior:

- Tooltip shows opening equity, closing equity, net PnL, fees, trade count, symbols traded.
- Supports date-range comparison and filter by trading mode (paper/live).
- Must clearly expose growth/decline of total money over sessions.

---

## 4) Data Definitions and Formulas

- Opening Total Equity:
	`OpeningCashBalance + OpeningHoldingsMarketValue`

- Closing Total Equity:
	`ClosingCashBalance + ClosingHoldingsMarketValue`

- Session Realized PnL:
	`Sum(RealizedPnL for closed trades in session)`

- Session Net PnL:
	`SessionRealizedPnL + SessionUnrealizedPnLAtClose - SessionFees - SessionOtherCosts`

- Equity Change Amount:
	`ClosingTotalEquity - OpeningTotalEquity`

- Equity Change %:
	`(ClosingTotalEquity - OpeningTotalEquity) / OpeningTotalEquity`

- Distinct Symbols Traded:
	`Count(DISTINCT symbol where execution in session)`

Important:

- The matching method for realized PnL (`FIFO` or weighted average) must be fixed and documented.
- Day/session boundaries must be consistent across backend jobs and frontend UI.

---

## 5) API Requirements (Executor.API or Reporting API)

## A. Session Summary Endpoints

- `GET /api/trading/report/sessions/daily?date=YYYY-MM-DD`
	Returns 6 session summary rows for the selected day.

- `GET /api/trading/report/sessions/range?from=YYYY-MM-DD&to=YYYY-MM-DD`
	Returns session summary rows across date range.

- `GET /api/trading/report/sessions/{sessionId}`
	Returns detail for one session including balances, PnL, trades, symbols.

## B. Session Activity Endpoints

- `GET /api/trading/report/sessions/{sessionId}/symbols`
	Returns symbol-level buy/sell/PnL breakdown for that session.

- `GET /api/trading/report/sessions/{sessionId}/trades`
	Returns trade-level records with timestamps, side, qty, price, fee, strategy.

## C. Equity Chart Endpoint

- `GET /api/trading/report/sessions/equity-curve?from=...&to=...`
	Returns ordered points for opening/closing equity and session net PnL.

---

## 6) Database and Persistence Requirements (Permanent Storage)

This report data must be stored permanently for long-term tracking and historical analysis.

## A. Mandatory Persistence Policy

- Session report data must not be auto-deleted by retention jobs.
- Historical session records are immutable audit data.
- Corrections are stored as versioned updates, not destructive overwrite.
- Backups must include all session reporting tables.

## B. Required Tables (Recommended)

1. `session_reports`
- `id` (PK)
- `report_date`
- `session_id` (unique per date + mode)
- `trading_mode` (`paper`/`live`)
- `session_start_utc`
- `session_end_utc`
- `opening_cash_balance`
- `opening_holdings_value`
- `opening_total_equity`
- `closing_cash_balance`
- `closing_holdings_value`
- `closing_total_equity`
- `realized_pnl`
- `unrealized_pnl_at_close`
- `fees_total`
- `other_costs_total`
- `net_pnl`
- `equity_change_amount`
- `equity_change_percent`
- `trade_count_total`
- `buy_count`
- `sell_count`
- `rejected_count`
- `cancelled_count`
- `distinct_symbols_count`
- `symbols_csv`
- `is_flat_at_close`
- `calculation_version`
- `created_at`
- `updated_at`

2. `session_symbol_reports`
- `id` (PK)
- `session_report_id` (FK)
- `symbol`
- `buy_count`
- `sell_count`
- `buy_qty`
- `sell_qty`
- `avg_buy_price`
- `avg_sell_price`
- `realized_pnl`
- `fees`
- `net_pnl`
- `win_trades`
- `loss_trades`
- `created_at`

3. `session_equity_timeseries`
- `id` (PK)
- `session_report_id` (FK)
- `point_time_utc`
- `cash_balance`
- `holdings_value`
- `total_equity`
- `created_at`

## C. Indexing and Query Performance

Required indexes:

- `session_reports(report_date, trading_mode)`
- `session_reports(session_id, trading_mode)`
- `session_symbol_reports(session_report_id, symbol)`
- `session_equity_timeseries(session_report_id, point_time_utc)`

## D. Data Integrity Rules

- Unique constraint for one row per `(report_date, session_id, trading_mode, calculation_version)`.
- Foreign keys with `ON DELETE RESTRICT` to protect historical data.
- Numeric precision must avoid rounding drift for PnL and balances.

---

## 7) Frontend Requirements

Page: `4-Hour Session Report`

## A. Filters

- Date picker (single day)
- Date range picker
- Trading mode (`paper`/`live`)
- Symbol filter
- Strategy filter (optional)

## B. Widgets

- Session summary cards.
- Session financial table.
- Session trading activity table.
- Session symbol breakdown table.
- Equity change by session chart (mandatory).

## C. UX Requirements

- Session order must always be S1 -> S6.
- PnL colors: green (profit), red (loss), gray (neutral).
- Show data freshness timestamp (`last updated`).
- Empty state and error state messages must be explicit.

## D. Export Requirements

- Export session summary to CSV.
- Export chart + summary snapshot to PDF (optional phase).

---

## 8) Validation and Testing Requirements

## A. Accuracy Checks

- Session net PnL must reconcile with raw orders/trades.
- Opening equity of session N+1 should match closing equity of session N, unless external cash adjustment exists (must be logged).
- Session-end flat status must match position records.

## B. Boundary Tests

- Correct mapping at exact boundary times (`04:00`, `08:00`, etc.).
- Correct behavior in liquidation window (`T_end - 30m` to `T_end`).
- No cross-session trade leakage.

## C. Performance Tests

- Daily report query returns within target SLA.
- Range query scales with historical data volume.

---

## 9) Acceptance Criteria

The report is complete when:

1. Users can see opening and closing balance for each of 6 sessions per day.
2. Users can see PnL for each session and total daily PnL.
3. Users can see trade count per session and symbols traded per session.
4. Users can view a clear chart of total equity changes across sessions.
5. All report data is permanently stored in the database for historical analysis.
6. Session values reconcile with raw execution and position data.
7. Session boundary and timezone logic is consistent and tested.

---

## 10) Recommended Delivery Phases

## Phase 1 (High Priority)

- Add session reporting schema and permanent persistence rules.
- Implement daily and range session summary APIs.
- Build core session financial table.

## Phase 2 (High Priority)

- Implement session symbol breakdown and activity APIs.
- Build equity change by session chart.
- Add reconciliation tests and boundary tests.

## Phase 3 (Medium Priority)

- Add export features (CSV/PDF).
- Add advanced diagnostics (rejections, slippage, latency by session).

This phased approach ensures correctness and auditability first, then expands analysis depth.
