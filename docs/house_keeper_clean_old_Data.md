# HouseKeeper Plan: System-Wide Data Hygiene and Automated Cleanup

## 1) Objective

This document defines a practical plan to:

1. Verify end-to-end service health and data flow.
2. Review data lifecycle and cleanup logic in code.
3. Build a dedicated automated HouseKeeper service for safe cleanup of excess data.
4. Identify and remove data artifacts that are no longer used.

Scope includes PostgreSQL, Redis, service-generated records, and repository artifacts.

## 2) Current-State Assessment (From Code Review)

### 2.1 Service landscape and data producers

1. Ingestor, Analyzer, Strategy, RiskGuard, Executor, Notifier, HistoricalCollector, Gateway are wired in Docker Compose and operate as the live pipeline.
2. Main persistent data stores:
	- PostgreSQL: `historical_collector.price_YYYY_ticks`, `historical_collector.data_gaps`, `public.orders`, plus support tables/views.
	- Redis: pub/sub channels and RiskGuard persistence keys.

### 2.2 Existing cleanup behavior already present

1. RiskGuard Redis state already has TTL behavior:
	- Cooldowns and validation/counter keys expire automatically.
2. Yearly partitioning exists for historical price ticks:
	- New yearly tables can be auto-created by DB function.

### 2.3 Gaps found (what is missing)

1. No centralized automated retention policy for PostgreSQL tables.
2. `historical_collector.data_gaps` has no purge/archive flow for old filled gaps.
3. `public.orders` has no retention/archive strategy for long-term growth.
4. No single housekeeping worker for scheduled cleanup + audit logs + dry-run mode.
5. No canonical "unused-data inventory" process (table-by-table verification with evidence).

### 2.4 Potentially unused or low-value persisted data (needs confirmation before delete)

From current code references:

1. `public.system_settings` is used by Gateway (`SystemSettingsRepository`) -> keep.
2. `historical_collector.data_gaps` is used by HistoricalCollector and Dashboard -> keep, but purge old filled rows.
3. `public.orders` is used by Executor, RiskGuard stats, and Gateway dashboards/reports -> keep, but enforce retention/archive.
4. `active_symbols`, `account_balance`, `trade_signals` are created in `infrastructure/init.sql`, but no active C# service reads/writes were found in current source scan.

Action: treat these three tables as "candidate unused" only after runtime verification in staging and dashboard/API behavior checks.

### 2.5 Build/operational note

Solution build succeeds for most services; Gateway build fails only when `Gateway.API.exe` is already running and file-locked. This is an operational lock issue, not a business-logic issue.

## 3) Data Hygiene Policy (Target State)

### 3.1 Retention classes

1. Hot operational data (fast query): keep short-to-medium retention.
2. Compliance/audit data: keep longer, optionally archive first.
3. Derived or refillable data: aggressive cleanup allowed.

### 3.2 Proposed default retention (initial)

1. `historical_collector.price_YYYY_ticks`
	- Keep 12 months hot in Postgres.
	- Archive older yearly partitions before drop.
2. `historical_collector.data_gaps`
	- Filled rows: keep 30-90 days then purge.
	- Unfilled rows: never auto-delete.
3. `public.orders`
	- Keep 12-24 months hot.
	- Archive old closed/failed rows older than policy threshold.
	- Never auto-delete current OPEN rows.
4. Redis riskguard keys
	- Keep existing TTL strategy.

## 4) HouseKeeper Service Design

### 4.1 Service type

Create new worker service:

1. Name: `HouseKeeper.Worker`
2. Runtime: `BackgroundService` with daily scheduled jobs.
3. Dependencies: PostgreSQL, Redis (optional), logging/metrics.

### 4.2 Job modules

1. `DataGapsCleanupJob`
	- Delete `filled_at IS NOT NULL` rows older than retention.
2. `OrdersArchiveAndCleanupJob`
	- Move old CLOSED/FAILED rows to archive table.
	- Delete from hot table in batches.
3. `PriceTicksPartitionJob`
	- Identify partitions older than retention.
	- Archive export + optional drop partition.
4. `UnusedTableAuditJob`
	- Report row count, last update, and reference status for candidate tables.
	- Never auto-drop tables in v1.

### 4.3 Safety controls (mandatory)

1. Dry-run mode by default in first rollout.
2. Row limit per batch (example: 5,000 rows/transaction).
3. Time-box each run (example: max 10 minutes).
4. Kill switch env var: disable all destructive actions immediately.
5. Cleanup report persisted to an audit table and structured logs.

### 4.4 Config model (example)

1. `HouseKeeper__Enabled=true|false`
2. `HouseKeeper__DryRun=true|false`
3. `HouseKeeper__ScheduleUtc=03:15`
4. `HouseKeeper__Retention__OrdersDays=365`
5. `HouseKeeper__Retention__FilledGapsDays=60`
6. `HouseKeeper__Retention__PriceTicksMonths=12`
7. `HouseKeeper__BatchSize=5000`
8. `HouseKeeper__MaxRunSeconds=600`

## 5) Logic Review and Unused Data Removal Strategy

### 5.1 Verification-first workflow

For each candidate unused table/data source:

1. Static code scan (already started).
2. Runtime query trace in staging.
3. Dashboard/API regression test.
4. Mark as one of:
	- Used and required.
	- Used but replaceable.
	- Unused and removable.

No direct DROP in production without passing all 4 checks.

### 5.2 Candidate removal phases

1. Phase A: stop writes.
2. Phase B: backup snapshot.
3. Phase C: read-only freeze period (7-14 days).
4. Phase D: remove references/migrations.
5. Phase E: drop table/column.

## 6) SQL Examples for HouseKeeper Jobs

### 6.1 Purge old filled gaps

```sql
DELETE FROM historical_collector.data_gaps
WHERE filled_at IS NOT NULL
  AND filled_at < NOW() - INTERVAL '60 days';
```

### 6.2 Archive and delete old failed/closed orders (pattern)

```sql
-- 1) Insert to archive
INSERT INTO archive.orders_history
SELECT *
FROM public.orders
WHERE status IN ('CLOSED', 'FAILED')
  AND time < NOW() - INTERVAL '365 days';

-- 2) Delete from hot table
DELETE FROM public.orders
WHERE status IN ('CLOSED', 'FAILED')
  AND time < NOW() - INTERVAL '365 days';
```

### 6.3 Candidate unused table evidence query

```sql
SELECT
  schemaname,
  relname AS table_name,
  n_live_tup AS estimated_rows,
  last_vacuum,
  last_autovacuum,
  last_analyze,
  last_autoanalyze
FROM pg_stat_user_tables
WHERE relname IN ('active_symbols', 'account_balance', 'trade_signals');
```

## 7) End-to-End System Check Checklist

Before enabling destructive cleanup:

1. Build all services without active file lock.
2. Verify service health endpoints.
3. Confirm pipeline flow: price -> signal -> risk -> order -> notify.
4. Verify dashboard pages still load and query correctly.
5. Snapshot database (backup).
6. Run HouseKeeper in dry-run and review report.
7. Enable cleanup for one job at a time.

## 8) Delivery Roadmap

### Phase 1 (1-2 days): Discovery and baseline

1. Finalize unused-data inventory.
2. Define retention policies with owner approval.
3. Add audit table schema for cleanup reports.

### Phase 2 (2-3 days): Implement HouseKeeper.Worker v1

1. Scaffold worker service.
2. Implement `DataGapsCleanupJob` and dry-run logging.
3. Add configuration and kill switch.

### Phase 3 (2-3 days): Orders archive/cleanup

1. Add archive table migration.
2. Implement batched archive + delete job.
3. Add metrics and failure alerts.

### Phase 4 (1-2 days): Hardening and production rollout

1. Staging validation and performance check.
2. Canary rollout in production.
3. Enable full schedule after 3 successful runs.

## 9) Acceptance Criteria

1. No service regression after cleanup runs.
2. Dashboard/API latency remains stable or improves.
3. Database growth rate decreases measurably.
4. All cleanup actions are auditable.
5. Candidate unused data is removed only with signed evidence.

## 10) Immediate Next Actions

1. Approve initial retention values.
2. Confirm whether `active_symbols`, `account_balance`, `trade_signals` should remain.
3. Start implementing `HouseKeeper.Worker` with dry-run mode only.
