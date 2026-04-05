# Daily Trading Report: Detailed Analysis and Implementation Plan

## 1) Goal and Scope

This document defines a detailed reporting page for daily trading activity so users can clearly understand:

1. What was bought and what was sold.
2. How many buy/sell actions were made per coin in a day.
3. How many buy/sell actions were profitable or losing.
4. Daily profit and loss (PnL).
5. Types of transactions performed.
6. Current owned capital flow (cash vs coin holdings).
7. Buy/sell timestamps and average holding time per trade.

The report is designed for live trading operations.

---

## 2) Main Report Sections (User-Friendly View)

## A. Trade Summary (Top Cards)

Display at the top of the page:

- Total Trades Today
- Buy Orders Today
- Sell Orders Today
- Winning Trades / Losing Trades
- Realized PnL Today
- Unrealized PnL Now
- Net Account Change Today
- Current Cash Balance
- Current Market Value of Holdings
- Total Equity (Cash + Holdings)

Why users need this:
- Gives an immediate status of system performance and current capital.

## B. Bought and Sold Assets (What was traded)

Table: Daily Trades by Symbol

Columns:
- Symbol
- Buy Count
- Sell Count
- Buy Quantity (sum)
- Sell Quantity (sum)
- Avg Buy Price
- Avg Sell Price
- Realized PnL
- Fees
- Net PnL
- Last Trade Time

Why users need this:
- Quickly see which coins are actively traded and which coins are profitable.

## C. Buy/Sell Frequency per Coin (In one day)

Visualization:
- Stacked bar chart by symbol: Buy Count vs Sell Count.
- Optional hourly heatmap: number of trades per hour per symbol.

Why users need this:
- Understand trading intensity, potential overtrading, and strategy behavior.

## D. Winning vs Losing Trades

Metrics:
- Win Rate (%)
- Loss Rate (%)
- Number of winning trades
- Number of losing trades
- Average Win
- Average Loss
- Profit Factor = Gross Profit / Abs(Gross Loss)

Breakdown options:
- By symbol
- By strategy
- By side (BUY then closed by SELL, SELL then closed by BUY if shorting is enabled)

Why users need this:
- Distinguish high-frequency activity from real strategy quality.

## E. Current Holdings and Cash Status

Table: Current Portfolio Snapshot

Columns:
- Symbol
- Quantity Held
- Average Entry Price
- Current Price
- Market Value
- Unrealized PnL
- Unrealized PnL %
- Portfolio Weight %

Separate panel:
- Available Cash
- Reserved Cash (if pending orders)
- Total Equity

Why users need this:
- Answer "How many coins do I currently have?" and "How much cash is left?" clearly.

## F. Daily PnL Detail

Metrics:
- Realized PnL (closed positions)
- Unrealized PnL (open positions)
- Trading Fees
- Funding/Borrowing Cost (if margin/futures later)
- Net PnL = Realized + Unrealized - Fees - Other Costs

Visualization:
- Intraday cumulative PnL line chart.
- PnL by symbol bar chart.

Why users need this:
- Understand not only final result but also intraday profit/loss path.

## G. Transaction Types Executed

Categories to report:
- Market Buy
- Market Sell
- Limit Buy
- Limit Sell
- Stop Loss Triggered
- Take Profit Triggered
- Manual Close (if enabled)
- System Rejection (risk rule blocked)

Table includes:
- Type
- Count
- Success Rate
- Avg Slippage
- Avg Execution Time

Why users need this:
- Verify execution quality and monitor strategy safety behavior.

## H. Capital Flow and Ownership Trend

Visualization set:
- Area chart: Cash vs Coin Value over time.
- Equity curve: Total account value over time.
- Allocation pie chart: % by symbol + cash.

Why users need this:
- See whether capital is concentrated in cash or exposed to market risk.

## I. Time Analytics (Buy/Sell time + average cycle)

Metrics:
- Trade Open Time
- Trade Close Time
- Holding Duration per trade
- Average Holding Duration (all trades)
- Average Holding Duration by symbol
- Average Time to Profit vs Time to Loss

Visualization:
- Histogram of holding duration.
- Scatter chart: duration vs trade PnL.

Why users need this:
- Validate whether trade duration matches strategy design.

---

## 3) Data Definitions and Formulas

Use consistent formulas across backend and frontend.

- Realized PnL (long):
	(ExitPrice - EntryPrice) * Quantity - Fees

- Unrealized PnL (long):
	(CurrentPrice - AvgEntryPrice) * OpenQuantity - EstimatedExitFees

- Win Rate:
	WinningClosedTrades / TotalClosedTrades

- Net Daily PnL:
	Sum(RealizedPnL of today) + CurrentUnrealizedPnL - FeesToday - OtherCostsToday

- Total Equity:
	CashBalance + Sum(CurrentPrice * OpenQuantity)

- Holding Duration:
	CloseTime - OpenTime

Important note:
- Closed trade matching should follow a consistent method (FIFO or weighted average) and be explicitly documented.

---

## 4) Proposed Backend Enhancements

## A. API Endpoints (Executor.API)

Add or extend endpoints:

- GET /api/trading/report/daily?date=YYYY-MM-DD
	Returns all daily summary metrics and section-level aggregates.

- GET /api/trading/report/daily/symbols?date=YYYY-MM-DD
	Returns per-symbol buy/sell counts and PnL.

- GET /api/trading/report/portfolio
	Returns current holdings, cash, and equity.

- GET /api/trading/report/capital-flow?from=...&to=...
	Returns time series for cash/holdings/equity.

- GET /api/trading/report/time-analytics?date=YYYY-MM-DD
	Returns duration and lifecycle metrics.

## B. Data Persistence Requirements

To support complete reporting, each order record should include:

- order_id
- symbol
- side
- order_type
- strategy_name
- requested_qty
- filled_qty
- requested_price
- filled_price
- status
- fee_amount
- slippage
- open_time
- close_time (if closed)
- linked_position_id
- realized_pnl
- is_paper_trade
- rejection_reason (if blocked)
- created_at

Also maintain a position snapshot source for fast portfolio view.

---

## 5) Proposed Frontend Report Page Structure

Page: Trading Daily Report

1. Filters Row
- Date picker
- Symbol multi-select
- Strategy select
- Trading mode (Paper/Live)

2. KPI Cards
- Core daily and portfolio metrics.

3. Section Tabs
- Overview
- Symbol Breakdown
- PnL Analysis
- Capital Flow
- Time Analytics
- Execution Quality

4. Export Actions
- Export CSV (table data)
- Export PDF snapshot (charts + summary)

5. UX Notes
- Show metric tooltip definitions.
- Color standard: green profit, red loss, neutral gray for inactive.
- Add "last updated" timestamp and data freshness indicator.

---

## 6) Detailed Reporting Additions for Better Understanding

To help users easily understand ongoing transactions and current capital, add these details:

- Signal-to-Execution Funnel:
	Signals generated -> passed risk checks -> executed -> closed.

- Rejection Reason Breakdown:
	Count by reason (max drawdown hit, cooldown active, position limit exceeded).

- Fee Impact Panel:
	Gross PnL vs Net PnL after fees.

- Slippage Monitor:
	Expected price vs fill price distribution.

- Exposure and Concentration:
	Top symbol exposure percentage and concentration risk warning.

- Daily Session Segmentation:
	Morning/Afternoon/Evening performance split.

- Stability Indicators:
	Average execution latency, failed order ratio, API error count.

---

## 7) Implementation Plan (Next System Development)

## Phase 1: Data Foundation (Priority High)
- Finalize order and position schema fields required by reports.
- Ensure every execution writes fee, slippage, timestamps, and status reason.
- Implement closed-trade matching logic consistently.

Deliverable:
- Reliable, query-ready trading dataset.

## Phase 2: Reporting APIs (Priority High)
- Implement daily report aggregate endpoint.
- Implement symbol-level and portfolio snapshot endpoints.
- Add capital-flow and time-analytics endpoints.
- Add endpoint contract tests.

Deliverable:
- Stable API layer for dashboard.

## Phase 3: Frontend Reporting UI (Priority High)
- Build report page with sections and charts described above.
- Add filters and drill-down navigation.
- Add empty/loading/error states with clear messages.

Deliverable:
- Usable report interface for daily operation.

## Phase 4: Validation and Accuracy (Priority High)
- Reconcile report values with raw trade logs.
- Verify PnL formulas with deterministic test cases.
- Add timezone consistency tests for daily boundaries.

Deliverable:
- Trusted metrics for decision-making.

## Phase 5: Operational Readiness (Priority Medium)
- Add alerting for abnormal loss and API failures.
- Add CSV/PDF export.
- Add trend comparison: today vs previous day/week.

Deliverable:
- Production-ready reporting and monitoring.

---

## 8) Acceptance Criteria

The report is considered complete when:

1. Users can identify exactly what was bought/sold and when.
2. Users can see buy/sell count by coin for any selected day.
3. Users can see winning/losing trades and win rate clearly.
4. Users can see current holdings, cash, and total equity in real time.
5. Users can see accurate daily PnL and per-symbol PnL.
6. Users can see transaction type distribution and execution quality.
7. Users can see capital flow evolution and average holding duration.
8. Exported reports match on-screen values.

---

## 9) Suggested Next Action

Start with Phase 1 and Phase 2 first. Without complete and consistent trade data, the frontend report can look correct but still produce misleading metrics.
