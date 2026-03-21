# Safety and Risk Guard Transparency Improvement Plan

## 1. Problem Statement

The current UI displays successful evaluation attempts for trading orders, but it does not clearly explain:

- What price data was used
- Why an evaluation passed
- Whether the final outcome is considered `Safe` or `Risk`
- Which specific rules led to the final result

This creates a visibility gap for users and makes post-trade auditing difficult.

## 2. Objectives

1. Make every safety/risk decision explainable to users.
2. Persist evaluation details for audit, troubleshooting, and analytics.
3. Expose a clear summary and detailed view in the UI.
4. Keep compatibility with current trading flow and avoid blocking order execution.

## 3. Scope

In scope:

- RiskGuard evaluation output structure
- Persistence model for evaluation records
- API contract for querying evaluation history
- UI improvements for list and detail views
- Logging, tracing, and metrics for evaluation lifecycle

Out of scope (Phase 1):

- Rule engine redesign
- Historical backfill for old orders without captured evaluation details
- AI-generated recommendation text

## 4. Target User Experience

For each evaluated order, users should be able to see:

- Evaluation status: `Safe`, `Risk`, or `Rejected`
- Final decision reason (human-readable)
- Key price context (symbol, entry/reference price, current price, spread/slippage if available)
- Rule-by-rule outcomes
- Timestamp, correlation ID, and latency

UI should provide:

- Compact table for history
- Expandable detail panel per evaluation
- Filters by symbol, time range, outcome, and strategy session

## 5. Proposed Domain Model

### 5.1 Top-Level Evaluation Record

Suggested record fields:

- `EvaluationId` (GUID)
- `OrderRequestId` (string or GUID)
- `SessionId` (nullable)
- `Symbol`
- `Side` (`Buy`/`Sell`)
- `RequestedQuantity`
- `RequestedPrice` (nullable for market orders)
- `MarketPriceAtEvaluation`
- `Outcome` (`Safe`, `Risk`, `Rejected`)
- `FinalReasonCode`
- `FinalReasonMessage`
- `RiskScore` (optional numeric score)
- `EvaluatedAtUtc`
- `EvaluationLatencyMs`
- `CorrelationId`
- `RawContextJson` (optional compact context payload)

### 5.2 Rule-Level Evaluation Detail

Each evaluation should include an ordered list of rule checks:

- `RuleName`
- `RuleVersion`
- `Result` (`Pass`, `Warn`, `Fail`, `Skipped`)
- `ReasonCode`
- `ReasonMessage`
- `ThresholdValue` (optional)
- `ActualValue` (optional)
- `MetadataJson` (optional)
- `DurationMs`

## 6. Data Persistence Design

### 6.1 Database Tables

Add two tables:

1. `risk_evaluations`
2. `risk_evaluation_rule_results`

`risk_evaluations` stores one row per evaluation.

`risk_evaluation_rule_results` stores one-to-many rule outcomes linked by `evaluation_id`.

### 6.2 Indexing

Recommended indexes:

- `risk_evaluations(symbol, evaluated_at_utc DESC)`
- `risk_evaluations(outcome, evaluated_at_utc DESC)`
- `risk_evaluations(session_id, evaluated_at_utc DESC)`
- `risk_evaluation_rule_results(evaluation_id, rule_name)`

### 6.3 Retention

Define configurable retention policy, for example:

- Hot storage: 90 days
- Archive/export after 90 days

Retention should be configurable through system settings.

## 7. Service-Level Changes

### 7.1 Shared Contracts (`src/Shared`)

Add/extend DTOs for:

- `RiskEvaluationResultDto`
- `RiskRuleResultDto`
- `RiskEvaluationOutcome` enum

Ensure all fields required by UI and audit are included.

### 7.2 RiskGuard API (`src/Services/RiskGuard/RiskGuard.API`)

Implement pipeline changes:

1. Collect per-rule result details during evaluation.
2. Build a normalized final outcome object.
3. Persist record asynchronously (non-blocking with bounded retry).
4. Return enriched response to caller (Strategy/Executor path).

Design note:

- If persistence fails, trading decision should continue, but a high-severity telemetry event must be emitted.

### 7.3 Optional Event Publishing

Publish evaluation summary to Redis stream/topic for real-time UI updates:

- Example stream: `risk:evaluations`

This is optional in Phase 1, recommended in Phase 2.

## 8. API Contract for UI

Add read endpoints in RiskGuard API or a dedicated query API:

- `GET /api/risk-evaluations`
	- Query params: `symbol`, `outcome`, `from`, `to`, `sessionId`, `page`, `pageSize`
- `GET /api/risk-evaluations/{evaluationId}`
	- Returns full detail including rule outcomes and context

Response shape should support:

- Fast paginated table rendering
- On-demand detail fetch for selected row

## 9. UI Improvement Plan (`frontend`)

### 9.1 History Table

Columns:

- Time
- Symbol
- Side
- Market Price
- Outcome
- Final Reason
- Latency
- Action (`View details`)

### 9.2 Detail Drawer/Modal

Sections:

1. Order Context
2. Price Context
3. Final Decision Summary
4. Rule-by-Rule Evaluation

Use color mapping:

- `Safe`: green
- `Risk`: amber
- `Rejected`: red

### 9.3 Filters and Search

Include:

- Symbol selector
- Outcome selector
- Date/time range picker
- Session ID search (optional)

## 10. Observability and Audit

### 10.1 Logging

Structured logs per evaluation:

- `evaluation_id`
- `symbol`
- `outcome`
- `final_reason_code`
- `latency_ms`

### 10.2 Metrics

Prometheus metrics:

- `risk_evaluation_total{outcome, symbol}`
- `risk_evaluation_duration_ms`
- `risk_rule_fail_total{rule_name, symbol}`
- `risk_evaluation_persistence_fail_total`

### 10.3 Tracing

Attach spans around:

- Rule evaluation
- Persistence write
- API response generation

Propagate correlation ID across Strategy -> RiskGuard -> Executor.

## 11. Security and Privacy

- Do not expose secrets or internal credentials in `ReasonMessage`.
- Sanitize metadata before persistence.
- Apply role-based access for detailed evaluation endpoints if needed.

## 12. Rollout Plan

### Phase 1: Foundations

1. Add DB schema and repository layer.
2. Add DTOs and enriched RiskGuard response.
3. Persist top-level + rule-level evaluation details.
4. Expose basic query endpoints.

### Phase 2: UI Transparency

1. Add history table and filters.
2. Add detail drawer with rule-level explanation.
3. Add status badges and reason formatting.

### Phase 3: Real-Time and Analytics

1. Add streaming updates to UI.
2. Add dashboard widgets (safe/risk ratio, top failing rules).
3. Tune retention and archive jobs.

## 13. Testing Strategy

### 13.1 Unit Tests

- Rule result mapping to final outcome
- Reason code and reason message generation
- DTO serialization/deserialization

### 13.2 Integration Tests

- RiskGuard evaluation persistence
- API query filtering and pagination
- End-to-end flow Strategy -> RiskGuard -> Executor with correlation ID continuity

### 13.3 UI Tests

- Table rendering from API data
- Detail view correctness for all outcomes
- Filter behavior and empty states

## 14. Acceptance Criteria

1. Every evaluation has a persisted final outcome and reason.
2. Every evaluation contains rule-by-rule results.
3. UI shows both summary and detailed explanation.
4. Users can filter and inspect historical evaluations.
5. Missing persistence or processing errors are observable via logs and metrics.

## 15. Deliverables Checklist

- SQL migration scripts for new tables and indexes
- Shared DTO and enum updates
- RiskGuard persistence + enriched response
- Query API endpoints with pagination/filtering
- Frontend evaluation history + detail UI
- Unit/integration/UI tests
- Operational runbook update

## 16. Suggested Next Step

Start with Phase 1 by implementing schema migration and RiskGuard persistence first, then expose read endpoints so frontend integration can proceed in parallel.
