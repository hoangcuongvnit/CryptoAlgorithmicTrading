# UI Update Requirements: Price Logs Table and 1-Hour Price Change Chart

## 1. Goal

Improve dashboard performance and readability by:

1. Replacing full-table rendering with scroll-based incremental loading.
2. Limiting loaded rows to avoid UI slowdown.
3. Showing newest data first by default.
4. Adding a detailed chart for price changes in the last 1 hour for all tracked coins.

## 2. Scope

This update applies to all UI tables currently rendering full datasets (especially price log tables) and to the market overview area where charting can be displayed.

## 3. Functional Requirements

### 3.1 Table Virtual Scrolling / Incremental Loading

1. Tables must not render all rows at once.
2. Data should be loaded in chunks only when needed during scroll.
3. Initial load should display only the first chunk of newest rows.
4. Additional rows are fetched/appended when the user scrolls near the end of currently loaded rows.
5. Scrolling behavior must remain smooth without noticeable stutter.

### 3.2 Max Rows Loaded in UI

1. The UI must never keep more than 100 rows in the rendered list at the same time.
2. If more historical rows are requested, use a rolling window strategy (remove oldest rendered rows outside the current viewport window).
3. Avoid loading huge payloads that freeze the browser.

### 3.3 Sort Order

1. All relevant tables must be sorted by timestamp in descending order.
2. Newest record must appear at the top.
3. New incoming records must be inserted at the top while preserving order.

### 3.4 1-Hour Price Change Chart for All Tracked Coins

1. Add a detailed chart showing price changes in the last 60 minutes.
2. Include all tracked coins in the same chart area.
3. Time range is fixed to the latest 1 hour (no user selector/dropdown needed).
4. The chart should auto-refresh with new data points.
5. Visualization must be detailed enough to compare short-term movement among coins clearly.

## 4. Data and Refresh Requirements

1. Recommended chart granularity: 1 data point per minute per coin (60 points per coin for the fixed 1-hour range).
2. Table incremental loading should use cursor-based or timestamp-based pagination.
3. Polling or streaming updates must not break sort order.
4. New data updates should be merged without full re-render of the table.

## 5. Performance Requirements

1. Avoid full DOM rendering of large datasets.
2. Keep the table responsive on desktop and typical laptop hardware.
3. Scrolling and row updates should feel real-time and stable.
4. Chart rendering should remain smooth when showing multiple coin lines.

## 6. Acceptance Criteria

1. Table does not load all records at startup.
2. At any moment, rendered rows are capped at 100.
3. Loading of additional rows happens only when scrolling near the end.
4. Newest rows always appear first.
5. A fixed 1-hour multi-coin price change chart is visible and updates continuously.
6. No timeframe selector is shown for this chart.
7. UI remains responsive during continuous updates.

## 7. Expected Outcome (English)

After this update, the dashboard should feel significantly lighter and faster. Users will see the latest logs first, while older records load only when needed during scrolling. The table will stay performant by limiting active rendered rows to 100. In addition, users will have a clear, detailed 1-hour price movement chart for all tracked coins, enabling quick cross-coin comparison without any extra configuration.
