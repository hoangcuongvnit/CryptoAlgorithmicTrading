# Plan 3 — Visual Data Analytics Dashboard for Historical + Real-Time Market Data

## Objectives

- Build a human-friendly dashboard to inspect and analyze both historical and live market data.
- Make the database structure understandable through UI-level data exploration views.
- Provide multi-chart analysis workflows for trend, volatility, volume, quality, and coverage.
- Support fast operational checks (ingestion health, missing data, freshness, and anomalies).

## Phase Scope

- Data source scope:
	- `historical_collector.price_ticks` (unified view across yearly partitions)
	- `historical_collector.price_2025_ticks`, `historical_collector.price_2026_ticks` (optional direct table checks)
	- `historical_collector.data_gaps`
- Symbol scope: `BTCUSDT`, `ETHUSDT`, `BNBUSDT`, `SOLUSDT`, `XRPUSDT`.
- Time granularity scope:
	- Primary: `1m`
	- Optional comparison aggregation in UI: `5m`, `15m`, `1h`, `1d`
- Delivery scope: read-only dashboard (no order placement, no write-back editing).

## Product Decisions

| Decision | Choice | Rationale |
|---|---|---|
| **Dashboard stack** | Blazor Server in `src/Gateway/Gateway.API` | Reuse .NET stack, low integration cost, simple internal deployment |
| **Charting library** | Apache ECharts (JS interop) | Rich time-series support, candlestick + volume + heatmap + zoom |
| **Data access** | Dapper + parameterized SQL | Existing project style, fast and predictable queries |
| **Refresh mode** | Hybrid (manual + auto refresh) | User control for heavy queries, live mode for operations |
| **Timezone display** | UTC default + local toggle | Keeps DB-consistent interpretation while supporting human readability |
| **Data exploration** | Dedicated "Data Dictionary" + "Schema Explorer" pages | Helps users understand structure and constraints without SQL |

## Dashboard Information Architecture

### Page 1 — Executive Overview

Purpose: quick health summary of dataset and ingestion quality.

Widgets:
- Total rows by symbol and selected date range.
- Latest candle timestamp per symbol and freshness lag (seconds/minutes).
- Coverage score per symbol (expected vs actual 1m candles).
- Open data gaps count (from `data_gaps`, where `filled_at IS NULL`).
- Daily ingestion status timeline (success/missing/partial).

### Page 2 — Market Data Explorer

Purpose: deep visual analysis of OHLCV data.

Core controls:
- Symbol selector (single/multi).
- Date range picker.
- Interval selector (`1m`, `5m`, `15m`, `1h`, `1d`).
- Live mode toggle (auto refresh every 10–30s).

Charts:
- Candlestick chart with pan/zoom.
- Synchronized volume bars under candlestick.
- Multi-line close price comparison across symbols.
- Rolling volatility chart (e.g., standard deviation of returns).
- Drawdown chart (peak-to-trough percentage).

### Page 3 — Coverage & Data Quality

Purpose: verify continuity and data reliability for backtesting.

Charts and tables:
- Calendar heatmap of per-day candle counts (target 1440 for `1m`).
- Missing-candles bar chart by day/symbol.
- Gap duration distribution histogram (from `data_gaps`).
- Fill latency chart (`filled_at - detected_at`).
- Drill-down table listing exact gap windows.

### Page 4 — Data Structure Explorer

Purpose: help humans understand schema and partitions without reading SQL files.

Sections:
- Entity overview cards:
	- `price_ticks` (unified view)
	- yearly partition tables
	- `data_gaps`
- Column-level dictionary:
	- name, type, nullable, semantic meaning, example value
- Constraints and indexes:
	- unique keys, primary keys, major query indexes
- Partition routing explanation:
	- how year-based tables are used
	- why this improves performance and maintenance

### Page 5 — Query Workbench (Read-Only)

Purpose: flexible analysis by power users without exposing dangerous SQL write operations.

Features:
- Predefined templates (top N volatile days, monthly volume, missing-data report).
- Parameterized filters only (symbol/date/interval), no arbitrary DDL/DML.
- Result grid export to CSV.
- Saved views/bookmarks per user (local storage or profile table in later phase).

## Data Model Mapping for UI

## Core Dataset: Candles (`historical_collector.price_ticks`)

Required fields mapped to UI:
- `time` -> x-axis timestamp.
- `symbol` -> grouping/filter dimension.
- `interval` -> granularity context.
- `open`, `high`, `low`, `close` -> candlestick body/wick.
- `volume` -> histogram and liquidity indicators.

## Data Quality Dataset: Gaps (`historical_collector.data_gaps`)

Mapped fields:
- `symbol`, `interval` -> filter dimensions.
- `gap_start`, `gap_end` -> missing-window boundaries.
- `detected_at` -> gap detection event time.
- `filled_at` -> recovery completion time.

Derived metrics:
- Gap duration in minutes.
- Fill latency in minutes/hours.
- Unfilled gap age.

## Visual Analytics Catalog (Minimum Required Charts)

1. Candlestick + Volume (primary trading context).
2. Multi-symbol normalized performance chart (same baseline index).
3. Return distribution histogram (market regime insight).
4. Volatility regime timeline (rolling sigma bands).
5. Drawdown curve + max drawdown marker.
6. Intraday seasonality heatmap (hour-of-day activity).
7. Daily candle-count heatmap (coverage quality).
8. Missing data trend line (gaps/day).
9. Gap duration histogram.
10. Freshness lag gauge per symbol.

## Backend/API Plan

Create dashboard-focused read APIs in Gateway:

- `GET /api/dashboard/overview`
- `GET /api/dashboard/candles`
- `GET /api/dashboard/quality/coverage`
- `GET /api/dashboard/quality/gaps`
- `GET /api/dashboard/schema`
- `GET /api/dashboard/workbench/template/{id}`

API constraints:
- Max date range per request (for example 90 days at `1m`) to avoid heavy payloads.
- Pagination for tables.
- Cancellation token support for long queries.
- Strict input validation for symbol and interval.

## SQL Query Strategy

- Use parameterized SQL only (Dapper anonymous objects).
- Prefer time-bounded predicates (`time >= @start AND time < @end`).
- Use unified view for cross-year queries; use yearly table for targeted performance checks.
- Pre-aggregate large windows server-side (do not send raw millions of rows to browser).
- Add materialized summary views later if query latency exceeds SLA.

## UX Requirements

- Fast first paint with skeleton states.
- Consistent loading, empty, and error states for every widget.
- Hover tooltips on charts with OHLCV and derived values.
- Cross-chart sync for time range and cursor position.
- One-click reset for filters.
- Export chart image and CSV (where relevant).

## Security and Access

- Read-only service account for dashboard queries.
- Disable all mutating SQL routes from dashboard API.
- Add request-level rate limiting on heavy endpoints.
- Audit log for dashboard query templates execution.

## Delivery Plan

### Step 1 — Dashboard Foundation

- Create dashboard module in `Gateway.API` (pages/components/services).
- Add DB query service (Dapper).
- Implement global filter state (symbol/date/interval).

**Expected outcome:** app shell with navigation and data-connected overview cards.

### Step 2 — Core Charting Layer

- Integrate ECharts via JS interop.
- Implement candlestick + volume + synchronized zoom.
- Add multi-symbol line chart and normalized performance mode.

**Expected outcome:** primary market visualization is usable for daily analysis.

### Step 3 — Data Quality & Coverage

- Implement coverage calculations and heatmap.
- Add data gap trend, distribution, and detailed table.
- Add freshness lag monitors.

**Expected outcome:** users can quickly identify missing data and ingestion issues.

### Step 4 — Data Structure Explorer

- Build schema explorer page and data dictionary UI.
- Render table/column metadata from PostgreSQL system catalog.
- Add partition explanation and key index visibility.

**Expected outcome:** non-DB users understand data architecture without SQL scripts.

### Step 5 — Query Workbench + Export

- Add safe template-driven query panel.
- Provide CSV export for result grids.
- Add saved presets for common analyses.

**Expected outcome:** flexible self-service analysis without risking database integrity.

### Step 6 — Performance Hardening

- Add caching for repeated overview queries.
- Add server-side downsampling for large chart windows.
- Run load tests for concurrent dashboard users.

**Expected outcome:** stable response times under realistic user load.

## Milestones and Timeline (Suggested)

- **Day 1:** Foundation + navigation + overview cards.
- **Day 2:** Candlestick/volume + symbol comparison charts.
- **Day 3:** Coverage and gap analytics pages.
- **Day 4:** Schema explorer and data dictionary.
- **Day 5:** Query workbench, export, polish, and performance tuning.

## Definition of Done

1. Dashboard displays both historical and latest real-time data for all 5 target symbols.
2. Users can inspect data quality (coverage, gaps, freshness) visually without manual SQL.
3. Data structure page clearly explains tables, columns, constraints, and yearly partition model.
4. At least 10 analysis charts are available and responsive in typical date ranges.
5. All dashboard APIs are read-only, validated, and stable under multi-user load.

## Risks and Mitigations

- **Risk:** heavy `1m` range queries become slow.
	- **Mitigation:** enforce max window, aggregate server-side, add caching/materialized summaries.
- **Risk:** visual overload for end users.
	- **Mitigation:** phased layout, sensible defaults, progressive disclosure.
- **Risk:** timezone confusion when validating candles.
	- **Mitigation:** UTC-first display with explicit local-time toggle and labels.

---

Note: This plan assumes data ingestion/backfill from Plan 2 is already completed and stable. Plan 3 focuses on turning that dataset into a reliable human analytics experience.
