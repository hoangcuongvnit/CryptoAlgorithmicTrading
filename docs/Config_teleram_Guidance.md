# Telegram Configuration via Admin UI - System Design and User Guide

## 1. Purpose

This document defines a complete design for a visual Telegram connection feature in the Admin UI Settings page.

Goals:

- Let admins configure Telegram without editing env files manually.
- Validate credentials safely before saving.
- Apply changes with clear status feedback.
- Provide a guided setup so non-technical users can connect in minutes.

## 2. Current State (As-Is)

From the current codebase:

- Settings page currently supports timezone only.
- Gateway exposes `/api/settings/system` and `/api/settings/system/timezone`.
- Notifier service already has Telegram sending logic via `TelegramNotifier`.
- Notifier exposes read-only endpoints:
	- `/api/notifier/config`
	- `/api/notifier/stats`
- Telegram credentials are loaded from static config (`Telegram:BotToken`, `Telegram:ChatId`) at service startup.

Current gap:

- No UI for Telegram settings.
- No secure runtime update path for Telegram credentials.
- No user-friendly setup walkthrough in product UI.

## 3. Scope

In scope:

- New Telegram settings panel in Admin Settings page.
- New backend APIs to read, validate, save, and test Telegram connection.
- Secure storage and masking strategy for secrets.
- Guided user flow and troubleshooting instructions.

Out of scope (phase 1):

- Multi-channel notification providers (Email, Slack, Discord).
- Per-user notification preferences.
- Multi-chat routing rules.

## 4. UX Design (Admin Settings)

### 4.1 New Settings Section

Add a new card under System Settings:

- Section title: `Telegram Notifications`
- Fields:
	- `Enabled` (toggle)
	- `Bot Token` (password field, masked)
	- `Chat ID` (text input, numeric validation)
	- Optional: `Send Startup Alerts`, `Send Trade Alerts`, `Send Error Alerts` (checkboxes)
- Actions:
	- `Test Connection`
	- `Save Configuration`
	- `Send Test Message`

### 4.2 Status and Health UX

Display connection status badge:

- `Connected` (green)
- `Not Configured` (gray)
- `Invalid Credentials` (red)
- `Last Test Failed` (orange)

Display metadata:

- Last updated time
- Updated by
- Last successful test time
- Last error summary (if any)

### 4.3 Guided Setup Drawer

Provide collapsible helper panel with steps:

1. Create bot with BotFather.
2. Copy bot token.
3. Start chat with bot and send `/start`.
4. Retrieve Chat ID.
5. Paste into settings and run Test Connection.
6. Save and send test message.

Include warnings:

- Never share bot token.
- If token leaked, regenerate immediately in BotFather.

## 5. Target Architecture (To-Be)

### 5.1 High-Level Flow

1. Admin updates Telegram config in Settings UI.
2. Frontend calls Gateway settings API.
3. Gateway validates payload and writes encrypted/masked values to PostgreSQL settings table.
4. Gateway notifies Notifier service to reload config (or Notifier polls settings version).
5. Notifier rebuilds Telegram client in-memory.
6. Admin triggers test; result is shown immediately in UI.

### 5.2 Components to Extend

Frontend:

- `frontend/src/pages/SettingsPage.jsx`
- New hooks in `frontend/src/hooks/useDashboard.js` or dedicated settings hook file
- i18n keys in `frontend/src/i18n/locales/en/settings.json` and `frontend/src/i18n/locales/vi/settings.json`

Gateway API:

- Extend `src/Gateway/Gateway.API/Program.cs` with Telegram settings routes
- Extend repository pattern in `src/Gateway/Gateway.API/Settings/SystemSettingsRepository.cs`

Notifier:

- Add runtime config provider abstraction in Notifier service
- Support hot-reload credentials without process restart

## 6. Data Model and Storage

### 6.1 Recommended Table Strategy

Reuse `public.system_settings` with namespaced keys.

Suggested keys:

- `notifications.telegram.enabled`
- `notifications.telegram.botToken.encrypted`
- `notifications.telegram.chatId`
- `notifications.telegram.lastTestStatus`
- `notifications.telegram.lastTestAtUtc`
- `notifications.telegram.lastError`
- `notifications.telegram.updatedBy`

### 6.2 Security for Bot Token

Do not store plaintext token.

Recommended options (choose one standard and keep consistent):

- Option A: ASP.NET Core Data Protection for encryption at rest.
- Option B: External secret manager (future phase).

API response should never return full token. Return only:

- `isConfigured`
- `tokenMasked` (example: `123456:ABC...XYZ`)

## 7. API Design

### 7.1 Read Current Telegram Settings

`GET /api/settings/notifications/telegram`

Response example:

```json
{
	"enabled": true,
	"isConfigured": true,
	"tokenMasked": "123456:ABCD...WXYZ",
	"chatIdMasked": "12****89",
	"lastTestStatus": "success",
	"lastTestAtUtc": "2026-03-21T10:15:30Z",
	"lastError": null,
	"updatedBy": "admin@local",
	"updatedAtUtc": "2026-03-21T10:10:12Z"
}
```

### 7.2 Validate Credentials (Without Persist)

`POST /api/settings/notifications/telegram/validate`

Request:

```json
{
	"botToken": "<token>",
	"chatId": 123456789
}
```

Response:

```json
{
	"valid": true,
	"botUsername": "my_trade_alert_bot",
	"chatReachable": true,
	"message": "Connection successful"
}
```

### 7.3 Save Telegram Settings

`PUT /api/settings/notifications/telegram`

Request:

```json
{
	"enabled": true,
	"botToken": "<optional-new-token>",
	"chatId": 123456789,
	"updatedBy": "admin@local"
}
```

Behavior:

- If `botToken` omitted, keep existing encrypted token.
- Validate `chatId` numeric and non-zero.
- Persist and trigger Notifier config reload.

### 7.4 Send Test Message

`POST /api/settings/notifications/telegram/test-message`

Request:

```json
{
	"message": "Test message from Admin UI"
}
```

Response:

```json
{
	"success": true,
	"sentAtUtc": "2026-03-21T10:20:11Z"
}
```

## 8. Validation Rules

Frontend validation:

- `botToken` required only when first configuration or token replacement.
- `chatId` required and must be integer.
- Friendly error hints near each field.

Backend validation:

- Reject empty token when no existing token exists.
- Reject non-numeric `chatId`.
- Reject suspiciously short token.
- Rate-limit test endpoints to avoid abuse.

## 9. Notifier Runtime Reload Strategy

Recommended phase-1 approach:

- Gateway persists config then calls `POST /api/notifier/reload-config`.
- Notifier reads latest settings and recreates internal Telegram client safely.

Alternative approach:

- Notifier polls config version every 30-60 seconds.

Reload requirements:

- Thread-safe swap of Telegram client instance.
- No process restart required.
- Existing message pipeline continues during swap.

## 10. Audit and Observability

Add audit logs for:

- Validation attempts
- Configuration update success/failure
- Test message send success/failure
- Token changed events (without exposing value)

Metrics to add:

- `telegram_config_update_total`
- `telegram_test_message_total`
- `telegram_send_failure_total`

## 11. Step-by-Step User Guide (For Admins)

### Step 1: Create Telegram Bot

1. Open Telegram and search `@BotFather`.
2. Send `/newbot` and follow prompts.
3. Copy bot token from BotFather.

### Step 2: Get Chat ID

1. Open chat with your new bot.
2. Send `/start`.
3. Get chat ID using one of these methods:
	 - Use Telegram API `getUpdates`.
	 - Use an internal helper tool if provided by your team.

### Step 3: Configure in Admin UI

1. Open `Settings` page.
2. Go to `Telegram Notifications` section.
3. Turn on `Enabled`.
4. Paste `Bot Token` and enter `Chat ID`.
5. Click `Test Connection`.

Expected result:

- Status shows `Connected`.
- Telegram receives a confirmation message.

### Step 4: Save and Verify

1. Click `Save Configuration`.
2. Click `Send Test Message`.
3. Confirm message arrives in Telegram chat.
4. Check Overview or Notifier stats to confirm notification flow.

## 12. Troubleshooting Guide

Issue: `Invalid token`

- Regenerate token via BotFather.
- Ensure no extra spaces when pasting.

Issue: `Chat not reachable`

- Ensure user/group has started chat with bot.
- Re-check chat ID type (private chat, group, channel).

Issue: `Saved but no alerts`

- Check Notifier health endpoint.
- Check Notifier logs for send failures.
- Confirm alert categories are enabled.

Issue: `Works in test, not in production`

- Verify production database has latest settings.
- Verify config reload endpoint executed successfully.

## 13. Rollout Plan

Phase 1:

- Backend APIs + secure storage + test connection.
- Basic UI form in Settings page.

Phase 2:

- Setup wizard drawer with visual walkthrough.
- Better diagnostics and user-facing troubleshooting hints.

Phase 3:

- Fine-grained notification categories and schedule windows.

## 14. Acceptance Criteria

Functional:

- Admin can configure Telegram from UI without editing env files.
- Test connection returns clear pass/fail result.
- Saved config is applied to Notifier without full platform restart.
- Test message can be sent from UI.

Security:

- Bot token is never returned plaintext from API.
- Bot token is encrypted at rest.
- Sensitive values are masked in logs and responses.

UX:

- First-time user can complete setup in under 5 minutes.
- Error messages are actionable and non-technical.

## 15. Suggested Implementation Notes for This Repository

- Reuse the existing settings pattern already implemented for timezone in Gateway.
- Keep the same minimal API style in `Program.cs` for consistency.
- Add repository methods rather than embedding SQL in endpoint handlers.
- Keep Notifier REST management endpoints on HTTP/1.1 as currently designed.

---

This design provides a practical path from the current static Telegram configuration to a secure, user-friendly, runtime-manageable Admin UI workflow.
