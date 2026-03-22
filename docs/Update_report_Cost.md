# Unified Cost, PnL, and Cash Flow Reporting Plan

## 1) Purpose

This document defines a practical redesign of trading reports so daily and 4-hour session views use the same financial data model.

Primary goal:
- give an accurate and visual view of profit/loss by combining trade activity, cash flow, and equity movement.

Scope:
- Daily report (calendar day).
- Session report (6 sessions/day, 4-hour each).
- Capital/budget tracking integration (paper mode first, extensible to live mode).

---

## 2) Why Redesign Is Needed

Current report documents are rich in metrics, but they can diverge if they are calculated from different sources.

Main risks to fix:
- daily PnL differs from session totals;
- trade tables do not always explain equity changes;
- cash adjustments (deposit/withdraw/reset) are disconnected from trading results;
- users cannot clearly answer: "Did equity change because of trading performance or capital operations?"

Design principle:
- one source of truth for cash, holdings value, realized PnL, fees, and equity.

---

## 3) Unified Reporting Model

## A. Canonical Financial Layers

All report screens must read from these layers:

1. Execution Layer
- orders/trades, side, qty, fill price, fee, slippage, rejection reason, timestamps, strategy, symbol.

2. Capital Ledger Layer
- budget operations and system balance transitions:
	`INITIAL`, `SESSION_PNL`, `DEPOSIT`, `WITHDRAW`, `RESET`.

3. Snapshot Layer
- session and day snapshots for opening/closing values:
	cash, holdings market value, total equity, flat-at-close flag.

4. Aggregation Layer
- daily summary, session summary, symbol breakdown, and equity curves.

## B. Mandatory Dimensions

Every metric must support:
- trading mode (`paper`, `live`);
- timezone (from `Trading:SessionTimeZone`);
- date/session boundary keys;
- optional symbol and strategy filters.

---

## 4) Standard Definitions and Formulas

Use these formulas in both backend and frontend.

- Opening Equity:
	`OpeningCash + OpeningHoldingsValue`

- Closing Equity:
	`ClosingCash + ClosingHoldingsValue`

- Realized PnL:
	`Sum(ClosedTradePnL)`

- Session Net PnL:
	`SessionRealizedPnL + SessionUnrealizedPnLAtClose - SessionFees - SessionOtherCosts`

- Daily Net PnL:
	`Sum(SessionNetPnL for day)`

- Equity Change Amount:
	`ClosingEquity - OpeningEquity`

- Equity Change Percent:
	`(ClosingEquity - OpeningEquity) / OpeningEquity`

- ROI Percent:
	`(CurrentEquity - InitialCapital) / InitialCapital`

Important consistency rules:
- one fixed matching method for realized PnL (FIFO or weighted average);
- session boundaries always applied in session timezone;
- daily totals must reconcile with sum of 6 sessions;
- equity movement must be decomposed into trading PnL vs external capital operations.

---

## 5) Daily Report (Redesigned)

## A. Top KPI Cards

- Total Trades
- Buy/Sell Count
- Winning/Losing Trades
- Realized PnL (day)
- Unrealized PnL (now)
- Fees (day)
- Net PnL (day)
- Opening Equity
- Closing Equity / Current Equity
- Equity Change %
- Net Capital Adjustment (deposit - withdraw)

## B. Day Breakdown Sections

1. Symbol Performance Table
- symbol, buy/sell count, quantities, avg prices, realized PnL, fees, net PnL.

2. PnL Quality Table
- win rate, avg win/loss, profit factor, rejection count and reasons.

3. Cash Flow Timeline
- time-ordered events from ledger + session snapshots.

4. Intraday Equity Curve
- separate lines for cash, holdings value, and total equity.

5. Trading vs Capital Operations Waterfall
- explain daily equity delta with components:
	trading net PnL, fees, deposit/withdraw/reset effects.

---

## 6) Session Report (4-Hour, Redesigned)

## A. Session Financial Summary (S1-S6)

Required per session row:
- opening cash/holdings/equity;
- closing cash/holdings/equity;
- realized/unrealized/fees/net PnL;
- trade counts and symbols;
- flat-at-close;
- last updated timestamp.

## B. Session Activity and Symbol Breakdown

- orders submitted/filled/rejected/cancelled;
- buy/sell counts;
- distinct symbols and most active symbol;
- symbol-level buy/sell quantity, avg prices, net PnL, wins/losses.

## C. Session Equity View (Mandatory)

- line chart: closing equity by session sequence;
- optional opening equity line;
- optional net PnL bars;
- tooltip includes fees, trade count, symbols, and flat status.

---

## 7) Budget and Cash Flow Integration Rules

## A. Data Objects to Integrate

- `paper_trading_ledger`
- `session_capital_snapshot`
- enhanced order/trade records with `session_id`, fees, slippage, and PnL fields

## B. Reconciliation Logic

At minimum, validate:

1. Session continuity:
- `OpeningEquity(Sn+1)` equals `ClosingEquity(Sn)` unless ledger adjustment exists.

2. Daily continuity:
- day opening/closing values align with first/last session of the day.

3. Ledger impact correctness:
- sum of ledger adjustments for the period matches reported capital operation totals.

4. Flat boundary control:
- when rule requires flat-at-close, holdings at session end must be zero (or explicitly flagged).

## C. Reporting Interpretation Layer

Each report must show two distinct impact channels:
- Trading Performance Impact (PnL from strategy execution)
- Capital Operation Impact (deposit/withdraw/reset)

This separation prevents misleading conclusions when equity increases due to manual deposit, not trading quality.

---

## 8) API Plan (Aligned and Minimal Duplication)

## A. Daily APIs

- `GET /api/trading/report/daily?date=YYYY-MM-DD`
- `GET /api/trading/report/daily/symbols?date=YYYY-MM-DD`
- `GET /api/trading/report/capital-flow?from=...&to=...`
- `GET /api/trading/report/time-analytics?date=YYYY-MM-DD`

## B. Session APIs

- `GET /api/trading/report/sessions/daily?date=YYYY-MM-DD`
- `GET /api/trading/report/sessions/range?from=YYYY-MM-DD&to=YYYY-MM-DD`
- `GET /api/trading/report/sessions/{sessionId}`
- `GET /api/trading/report/sessions/{sessionId}/symbols`
- `GET /api/trading/report/sessions/equity-curve?from=...&to=...`

## C. Budget APIs

- `GET /api/trading/budget/status`
- `GET /api/trading/budget/ledger`
- `GET /api/trading/budget/equity-curve`
- `POST /api/trading/budget/deposit`
- `POST /api/trading/budget/withdraw`
- `POST /api/trading/budget/reset`

Contract guideline:
- use shared DTO fields for cash/equity/PnL definitions to avoid drift between endpoints.

---

## 9) Frontend UX Blueprint

Page set:
- Daily Trading Report
- 4-Hour Session Report
- Budget & Cash Flow Panel

Shared UX rules:
- same formula tooltips across pages;
- consistent color semantics (profit green, loss red, neutral gray);
- visible timezone label;
- explicit data freshness timestamp;
- clear empty/error states;
- CSV export for tables, PDF export as optional phase.

Recommended visual stack:
- KPI cards for overview;
- drill-down tables for symbol/session details;
- equity/cash flow charts for trend;
- waterfall or decomposition chart for "why equity changed".

---

## 10) Delivery Roadmap

## Phase 1 - Data and Formula Hardening (High)
- finalize canonical formulas and matching method;
- enforce timezone/session boundary helpers;
- ensure order and ledger write completeness.

## Phase 2 - Reconciliation Backbone (High)
- implement automated checks between session/day/ledger totals;
- add discrepancy flags and audit logs.

## Phase 3 - API Consolidation (High)
- align daily/session/budget endpoints to common DTO contracts;
- remove duplicated metric logic.

## Phase 4 - Frontend Integration (High)
- rebuild daily and session pages on unified model;
- add cash flow decomposition and budget context widgets.

## Phase 5 - Validation and Operations (Medium)
- deterministic test cases for PnL and boundary times;
- performance tests for range queries;
- monitoring alerts for reconciliation drift.

---

## 11) Testing and Validation Checklist

- boundary mapping at exact session cut points (`04:00`, `08:00`, `12:00`, `16:00`, `20:00`, `24:00`);
- liquidation-window behavior validation;
- no cross-session leakage for executions;
- daily totals = sum of 6 sessions;
- equity reconciliation with ledger + open positions;
- trade-level spot checks against report aggregates;
- API SLA checks for daily/session range queries.

---

## 12) Acceptance Criteria

The redesign is complete when:

1. Daily and session reports show consistent PnL/equity values from the same model.
2. Users can clearly separate trading performance from deposit/withdraw/reset effects.
3. Session and daily boundaries are timezone-correct and test-covered.
4. Cash, holdings value, and total equity trends are visible and explainable.
5. Reported values reconcile with raw execution and ledger data.
6. Historical records remain queryable for audit and strategy review.

---

## 13) Immediate Next Build Focus

Start with Phase 1 and Phase 2 before UI refinements.

Reason:
- if formulas and reconciliation are not locked first, charts can look correct but still present misleading profitability.

