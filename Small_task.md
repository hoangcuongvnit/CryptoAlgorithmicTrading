# RiskGuard History Empty After Rebuild - Root Cause and Fix Plan

## Problem Summary
- After rebuilding and running Docker Compose, RiskGuard management API may show empty runtime state:
	- `cooldowns: []`
	- potentially empty `recentValidations`
- This is confusing because trading may still be running and RiskGuard itself is healthy.

## Root Cause Analysis

### 1) Redis data is not durable across container recreation
- Current `infrastructure/docker-compose.yml` does not mount a persistent volume for the `redis` service.
- If Redis container is recreated (for example after `docker compose down` then `up`), in-memory data is lost.
- RiskGuard runtime recovery (`ValidationHistory`, `CooldownRule`) depends on Redis keys. If keys are gone, startup state is empty.

### 2) `cooldowns` endpoint behavior is easy to misread
- `/api/risk/stats` returns only active cooldowns (remaining time > 0).
- `Risk__CooldownSeconds` is typically 30s. After restart, most previously saved cooldown timestamps are already expired.
- Result: `cooldowns: []` is expected in many cases, even when Redis has stored historical cooldown timestamps.

### 3) Validation history is runtime cache + Redis restore
- `recentValidations` is restored from Redis key `riskguard:validations:yyyy-MM-dd`.
- If Redis data was lost due to container recreation, restore returns empty list.

## Proposed Fixes

### A. Infrastructure fix (required)
Persist Redis data using a named volume.

Update `infrastructure/docker-compose.yml`:
- Add Redis volume mount:
	- `- redis_data:/data`
- Add `redis_data` in top-level `volumes:` section.

Expected result:
- RiskGuard state survives container recreation and image rebuilds (unless explicitly removed with `docker compose down -v`).

### B. API clarity fix (recommended)
Improve `/api/risk/stats` response to reduce false alarms:
- Keep `cooldowns` for active cooldowns.
- Add `cooldownsStored` count loaded from persistence status endpoint or internal state.
- Add `cooldownSeconds` in payload so users understand active window.

Expected result:
- Operators can distinguish "no active cooldown" from "no data persisted".

### C. Logging/observability fix (recommended)
At startup, log with clearer context:
- Number of cooldown entries loaded.
- Number of active cooldowns after window filtering.
- Number of validation records restored.

Expected result:
- Faster diagnosis after deployments/restarts.

### D. Optional resilience improvements
- Persist today counters on every record (not only each 5th record) to reduce crash-window loss.
- Optionally expose a dedicated endpoint for raw persisted cooldown entries (active + expired) for diagnostics.

## Validation Checklist
1. Rebuild and restart services with Redis volume enabled.
2. Generate test risk evaluations.
3. Verify `/api/risk/stats` returns non-empty `recentValidations`.
4. Verify `cooldowns` becomes non-empty immediately after approvals and returns to empty after cooldown window expires.
5. Restart RiskGuard container only; verify history is restored from Redis.
6. Restart full stack (without `-v`); verify history is still restored.

## Notes for Operations
- If you run `docker compose down -v`, Redis data is intentionally deleted and state will reset.
- For persistence testing, avoid `-v`.
