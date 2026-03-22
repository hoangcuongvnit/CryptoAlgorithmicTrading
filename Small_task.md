# Small Task: Add always-available Telegram health check from saved DB credentials

## 1. Goal

Add a new backend API and a new UI button so an operator can verify Telegram integration at any time using credentials already saved in the database (without re-entering bot token and chat ID in the form).

## 2. Problem Statement

Current flows are tied to form state and may fail with `400 Bad Request` when token/chat ID are not provided in the request body.

Operational need:

- The system must support a quick, reliable check based only on stored configuration.
- This check should be available even when the user is not editing Telegram settings.

## 3. Scope

In scope:

- New API endpoint for health check using saved credentials.
- New UI button to trigger this check anytime.
- User feedback (success/failure message + optional metadata).

Out of scope:

- Replacing existing manual validation endpoint.
- Changing credential storage format.
- Telegram onboarding flow.

## 4. Functional Requirements

1. The system shall expose a new endpoint, for example:
	 - `POST /api/settings/notifications/telegram/health-check`
2. The endpoint shall read bot token and chat ID from DB/config storage.
3. The endpoint shall not require token/chat ID in request body.
4. The endpoint shall perform a live Telegram check (recommended sequence: `getMe`, then optional lightweight message send).
5. The endpoint shall return structured response with status and diagnostics.
6. UI shall include a new button, e.g. `Check Telegram Health`.
7. Clicking this button shall call the new endpoint and show clear toast/status feedback.
8. Button must be available whenever Telegram is configured (or always visible but disabled with explanation if not configured).

## 5. Non-Functional Requirements

1. Response time target for health check: under 5 seconds in normal network conditions.
2. API must never return raw token/chat ID in response.
3. Error messages must be operator-friendly and safe (no secrets in logs or UI).
4. UI must prevent duplicate click storms (disable button while request is running).

## 6. Proposed API Contract

### Request

- Method: `POST`
- URL: `/api/settings/notifications/telegram/health-check`
- Body: empty (or optional `{ "message": "..." }` if test message is supported)

### Success Response (example)

```json
{
	"success": true,
	"status": "healthy",
	"botUsername": "my_bot",
	"checkedAtUtc": "2026-03-22T08:40:00Z",
	"details": "Telegram connection is operational"
}
```

### Failure Response (example)

```json
{
	"success": false,
	"status": "unhealthy",
	"errorCode": "TELEGRAM_CONFIG_MISSING",
	"message": "Saved Telegram credentials were not found"
}
```

Recommended error codes:

- `TELEGRAM_CONFIG_MISSING`
- `TELEGRAM_AUTH_FAILED`
- `TELEGRAM_CHAT_UNREACHABLE`
- `TELEGRAM_NETWORK_ERROR`
- `TELEGRAM_UNKNOWN_ERROR`

## 7. UI Changes

Add a new action button in Telegram settings panel (next to existing actions):

- Label: `Check Telegram Health`
- Behavior:
	- On click -> call `/telegram/health-check`
	- Show loading state: `Checking...`
	- On success -> show green toast + optionally refresh status badge timestamp
	- On failure -> show red toast with API message

Suggested enable/disable rule:

- Enabled when `cfg?.isConfigured === true`
- Disabled otherwise with helper text: `Configure Telegram first`

## 8. Backend Processing Logic

1. Load Telegram settings record from DB.
2. Validate that encrypted token and chat ID exist.
3. Decrypt token (if encrypted at rest).
4. Call Telegram `getMe` to verify token validity.
5. Optionally send a lightweight health message to configured chat.
6. Persist latest check result (`lastHealthStatus`, `lastHealthAtUtc`, `lastHealthError`).
7. Return structured API response.

## 9. Security and Logging

1. Mask secrets in all logs.
2. Do not include full token/chat ID in exceptions sent to UI.
3. Add audit log entry for each manual health check action with `updatedBy`/operator identity if available.

## 10. Acceptance Criteria

1. Operator can click `Check Telegram Health` without entering token/chat ID.
2. System checks Telegram using credentials from DB.
3. Success path returns healthy response and UI displays success toast.
4. Missing config path returns clear business error (not generic 400 parser error).
5. Invalid token path returns clear authentication failure message.
6. Button is protected against repeated clicks while request is in progress.
7. No secret leakage in API response or logs.

## 11. Test Scenarios

1. Config exists + valid token/chat -> health check success.
2. Config missing -> `TELEGRAM_CONFIG_MISSING`.
3. Token revoked/invalid -> `TELEGRAM_AUTH_FAILED`.
4. Chat ID invalid/not accessible -> `TELEGRAM_CHAT_UNREACHABLE`.
5. Telegram API timeout/network issue -> `TELEGRAM_NETWORK_ERROR`.
6. UI double click during loading -> only one request is processed.

## 12. Implementation Notes

- Keep existing endpoints (`validate`, `validate-saved`, `test-message`) for backward compatibility.
- New `health-check` endpoint should become the default operator action for quick runtime verification.
- Reuse shared error mapping in frontend so backend messages are displayed consistently.
