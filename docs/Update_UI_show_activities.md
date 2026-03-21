# UI Update Plan: Show Real-Time System Activities as an Observable Log

## 1. Requirement Analysis

The goal is to make the system's current behavior visible to operators in real time, in a way similar to a system log.

From the request, the UI should clearly show:

1. What the system is doing right now.
2. Price update activities (market data flow).
3. Risk evaluation activities (which checks were executed).
4. The result of each risk evaluation (pass/reject + reason).

This is primarily an observability and operational UX requirement, not only a visual improvement.

## 2. Desired User Outcomes

After this update, an operator should be able to:

1. Open the dashboard and immediately understand the current system state.
2. Follow event flow chronologically from data ingestion to execution decision.
3. Detect abnormal behavior quickly (missing price updates, repeated risk rejections, service delay).
4. Investigate why an order was accepted/rejected without checking backend logs.

## 3. Scope Definition

### In Scope

1. A unified activity stream in the frontend UI.
2. Structured event types for core pipeline activities.
3. Human-readable rendering of event details and outcomes.
4. Filtering/searching by event type, symbol, severity, and status.
5. Auto-refresh or near real-time updates with stable performance.

### Out of Scope (Phase 1)

1. Full distributed tracing UI.
2. Historical analytics dashboard beyond recent event stream.
3. Advanced alert rules engine in UI.

## 4. Proposed Activity Event Model

Define a normalized activity event payload that frontend can render consistently:

```json
{
  "eventId": "uuid",
  "timestampUtc": "2026-03-21T08:30:15.000Z",
  "service": "Analyzer|Strategy|RiskGuard|Executor|DataIngestor",
  "category": "PRICE|SIGNAL|RISK_EVALUATION|ORDER|SYSTEM",
  "action": "PriceUpdated|IndicatorCalculated|RiskRuleEvaluated|OrderApproved|OrderRejected",
  "symbol": "BTCUSDT",
  "severity": "INFO|WARN|ERROR",
  "status": "SUCCESS|FAILED|REJECTED|SKIPPED",
  "message": "Short human-readable summary",
  "details": {
	 "ruleName": "MaxDrawdownRule",
	 "input": { "drawdownPercent": 8.2, "limit": 10.0 },
	 "result": { "passed": true, "reason": "Within threshold" }
  },
  "correlationId": "trace-or-request-id"
}
```

Notes:

1. `category`, `action`, `severity`, and `status` should be enums for predictable filtering.
2. `message` should always be operator-friendly and concise.
3. `details` should carry structured metadata for expandable rows/cards.
4. `correlationId` enables grouping all events for one trading decision chain.

## 5. UI/UX Plan (System Log View)

### 5.1 Main Components

1. Activity Log Panel (new or expanded `EventsPage`).
2. Live Status Header (connection state, last update time, events/sec).
3. Filter Bar (symbol, category, status, service, time range).
4. Log List with expandable rows for full details.
5. Quick stats chips (price events, risk checks passed/rejected, orders).

### 5.2 Log Row Information

Each row should include:

1. Timestamp (local + relative time).
2. Service name.
3. Category/action.
4. Symbol.
5. Result badge (SUCCESS/REJECTED/FAILED).
6. Short message.

Expanded area should include:

1. Risk rule input and threshold.
2. Evaluation result and rejection reason.
3. Correlation ID and linked events.

### 5.3 Visual Priorities

1. INFO events are neutral and compact.
2. WARN events are highlighted.
3. ERROR/FAILED/REJECTED events are visually prominent.
4. New events appear at top with smooth, lightweight animation.

## 6. Backend/Integration Plan

### 6.1 Event Sources

Collect activity events from:

1. `DataIngestor`: market price ingestion updates.
2. `Analyzer`: indicator calculations and signal generation.
3. `Strategy`: order intent generation.
4. `RiskGuard`: each risk rule evaluation and final decision.
5. `Executor`: execution result (paper/live), fill or failure details.

### 6.2 Transport to Frontend

Recommended order of implementation:

1. Phase 1: Extend existing polling endpoint in frontend hook to fetch latest events.
2. Phase 2: Optional upgrade to server-sent events or WebSocket for true real-time push.

### 6.3 Data Retention

1. Keep recent N events in memory/cache for fast UI (example: last 500-2000 events).
2. Keep durable audit in existing stream/storage for deeper investigation.

## 7. Frontend Implementation Plan

## Phase A: Foundation

1. Define frontend types/interfaces for `ActivityEvent`.
2. Create event normalization utility (map API payload to UI model).
3. Add category/status/severity constants.

## Phase B: Data Integration

1. Extend `useDashboard` or create `useActivities` hook.
2. Implement polling cadence with deduplication by `eventId`.
3. Handle connection/loading/error states clearly.

## Phase C: Activity Log UI

1. Build `ActivityLog` component (list + virtualization if needed).
2. Build `ActivityFilters` component.
3. Add expandable event detail drawer/row.
4. Integrate into `EventsPage`.

## Phase D: Risk Evaluation Visibility

1. Add dedicated rendering template for `RISK_EVALUATION` events.
2. Display rule name, threshold, measured value, pass/reject, reason.
3. Group by `correlationId` to show full decision chain.

## Phase E: Validation and Hardening

1. Add unit tests for normalization and filter logic.
2. Add component tests for rendering status badges and details.
3. Add performance checks for high-frequency event flow.

## 8. Acceptance Criteria

1. Operator can see live activity stream with latest events first.
2. Price updates are visible with symbol and timestamp.
3. Every risk rule evaluation shows result (pass/reject) and reason.
4. Final order decision is traceable from prior events.
5. Filters work for symbol/category/status/service.
6. UI remains responsive under expected event load.

## 9. Risks and Mitigations

1. High event volume may degrade rendering.
	- Mitigation: list virtualization, capped in-memory list, throttled updates.
2. Inconsistent event payloads across services.
	- Mitigation: shared schema contract + validation.
3. Duplicate or out-of-order events.
	- Mitigation: dedupe by `eventId`, sort by timestamp, display ingestion time fallback.
4. Missing context for risk decisions.
	- Mitigation: require `ruleName`, `passed`, and `reason` fields in risk events.

## 10. Suggested Delivery Milestones

1. Milestone 1 (1-2 days): Event schema + frontend hook + simple activity list.
2. Milestone 2 (1-2 days): Filters + detailed risk evaluation rendering.
3. Milestone 3 (1 day): Performance improvements + tests + UX polish.

## 11. Definition of Done

1. Activity log is visible in UI and updated continuously.
2. Risk evaluation lifecycle is transparent and understandable.
3. At least baseline automated tests cover parsing/filtering/rendering.
4. Documentation updated with event schema and UI usage notes.

