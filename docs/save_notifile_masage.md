# Notification Message Persistence and Recent Activity Integration

## Goal

Persist all successfully sent Telegram messages in PostgreSQL, then load and display those records in the Overview page under Recent Activity.

## Business Requirements

1. Every message that is successfully sent to Telegram must be stored in the database.
2. Stored data must survive service restarts (no in-memory-only history).
3. Recent Activity on Overview must read from persisted data.
4. API must expose recent notification messages for dashboard usage.
5. If database persistence is not configured, service should continue running with in-memory fallback.
6. UI must load only the 30 newest messages and sort by newest timestamp first.
7. UI must paginate those 30 rows into 3 pages (10 rows per page).
8. Each row must provide a button to copy the full notification message content.

## Data Model

Table: public.notifier_message_logs

- id (bigserial, PK)
- channel (varchar(20), default 'telegram')
- category (varchar(50), not null)
- summary (text, not null)
- message_text (text, not null)
- sent_at_utc (timestamptz, not null)
- external_message_id (text, nullable)
- created_at_utc (timestamptz, default now())

Indexes:

- idx_notifier_message_logs_sent_at (sent_at_utc DESC)
- idx_notifier_message_logs_category_sent_at (category, sent_at_utc DESC)

Migration script:

- scripts/add-notifier-message-log.sql

## Runtime Flow

1. Notifier formats outgoing Telegram message.
2. Notifier sends message via Telegram Bot API.
3. On success, Notifier stores one record into notifier_message_logs.
4. Overview calls notifier stats endpoint through Gateway.
5. API returns today counts + recentNotifications from DB.
6. UI renders recentNotifications in Recent Activity timeline.

## API Design

### Notifier Service

1. GET /api/notifier/stats

- Returns:
	- todayTotal
	- todayByCategory
	- recentNotifications[]

2. GET /api/notifier/messages?limit=50

- Returns recent persisted messages:
	- id
	- category
	- summary
	- message
	- timestampUtc
	- externalMessageId

### Gateway Proxy

1. GET /api/notifier/stats
2. GET /api/notifier/messages

Gateway forwards requests to Notifier service and returns raw JSON response.

## UI Integration

File: frontend/src/pages/OverviewPage.jsx

- Recent Activity now reads:
	- notifier.recentNotifications (primary, DB-backed)
	- notifier.recent (fallback for compatibility)

File: frontend/src/hooks/useDashboard.js

- Activity merge now uses recentNotifications first, then fallback to recent.
- Notifier stats request uses limit=30 to align with UI pagination design.

File: frontend/src/components/EventTimeline.jsx

- Sorts events by timestamp descending and keeps latest 30 rows.
- Paginates into exactly 3 pages (10 rows each).
- Adds a per-row Copy button that copies message content (full message when available, otherwise summary).

## Deployment Notes

1. Run migration script before deploying new Notifier build.
2. Provide ConnectionStrings__Postgres for Notifier service.
3. In docker-compose, Notifier now depends on Postgres and receives Postgres connection string.

## Non-Functional Notes

1. DB persistence failures must not block sending notifications.
2. API responses remain backward-compatible with the old fallback shape.
3. Limit values are clamped to protect API and DB from excessive fetch sizes.
