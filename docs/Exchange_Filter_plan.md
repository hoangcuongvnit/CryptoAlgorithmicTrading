# Exchange Filter Bug Fix Plan: LOT_SIZE and MIN_NOTIONAL

## 1) Problem Summary

Observed error:

- `Quantity 0.001 rounds to zero with LOT_SIZE step 0.10000000`

Root cause:

- The requested quantity is below the exchange `LOT_SIZE.stepSize`, so rounding down produces `0`.
- Another common rejection is `MIN_NOTIONAL`: order value is too small.
- For each symbol, Binance has different filter values (`stepSize`, `minQty`, `minNotional`, and sometimes `NOTIONAL`).

The system must validate and normalize order inputs using symbol-specific exchange filters before sending orders.

---

## 2) Required Exchange Data

Use Binance endpoint:

- `GET /api/v3/exchangeInfo`

For each trading symbol, read these filters:

- `LOT_SIZE.stepSize`
- `LOT_SIZE.minQty`
- `MIN_NOTIONAL.minNotional` (or `NOTIONAL.minNotional` if present)

Notes:

- Always guard against invalid values such as `stepSize <= 0`.
- Filter definitions can change over time, so cache with TTL and refresh on failure.

---

## 3) Quantity Normalization (LOT_SIZE)

Given:

- requested quantity: `q_req`
- symbol step size: `step`

Normalize with floor rounding:

$$
q_norm = \left\lfloor \frac{q_{req}}{step} \right\rfloor \times step
$$

Validation rules:

- If `step <= 0`: skip step rounding and log warning (do not divide by zero).
- If `q_norm <= 0`: reject early with actionable message (quantity too small for this symbol).
- If `minQty > 0` and `q_norm < minQty`: reject or auto-adjust upward (strategy decision).

Recommendation:

- Keep current safe behavior: floor rounding + early reject when result becomes zero.
- Return clear error message with required minimum tradable increment.

---

## 4) Notional Validation (MIN_NOTIONAL / NOTIONAL)

Compute order notional:

$$
notional = q_{norm} \times price
$$

Validation rules:

- If `minNotional > 0` and `notional < minNotional`, reject before calling place-order API.
- For market orders where request price is absent, estimate using best available reference:
	- best bid/ask,
	- or last traded price,
	- or weighted average price (depending on available API data).

Auto-suggestion for next attempt:

$$
q_{needed} = \left\lceil \frac{minNotional}{price \times step} \right\rceil \times step
$$

This gives the minimum valid quantity aligned to `stepSize`.

---

## 5) Redis Caching Strategy (Per Symbol)

Current status in code:

- Executor already caches symbol filters in-process (`ConcurrentDictionary`) for runtime reuse.

Gap:

- Cache is not shared across service restarts/instances.

Proposed Redis keys:

- `exchange:filters:{SYMBOL}` (JSON)
- `exchange:filters:last_updated:{SYMBOL}` (timestamp optional)

Suggested JSON payload:

```json
{
	"symbol": "BTCUSDT",
	"stepSize": "0.00100000",
	"minQty": "0.00100000",
	"minNotional": "5.00000000",
	"sourceFilter": "MIN_NOTIONAL",
	"updatedAtUtc": "2026-04-04T00:00:00Z",
	"version": 1
}
```

TTL recommendation:

- Default TTL: 6-24 hours.
- Force refresh when Binance rejects with filter-related error.

Load order:

1. Check in-memory cache.
2. If miss, check Redis.
3. If miss/stale, call Binance `exchangeInfo`.
4. Write back to Redis and in-memory cache.

---

## 6) Learn from Failure (Adaptive Recovery)

When an exchange rejection happens, persist failure metadata for future attempts.

Suggested Redis key:

- `exchange:reject:{SYMBOL}:{RULE}`

Example payload:

```json
{
	"symbol": "XRPUSDT",
	"rule": "MIN_NOTIONAL",
	"attemptedQty": "1",
	"attemptedPrice": "0.45",
	"attemptedNotional": "0.45",
	"requiredMinNotional": "5.00",
	"suggestedQty": "11.2",
	"lastError": "Filter failure: MIN_NOTIONAL",
	"createdAtUtc": "2026-04-04T00:00:00Z"
}
```

Use this data to:

- generate better quantity suggestions,
- avoid repeating known-invalid order sizes,
- improve observability (how often each rule blocks orders).

---

## 7) Recommended Execution Flow

Before placing any order:

1. Resolve symbol filters (`stepSize`, `minQty`, `minNotional`) via cache chain (memory -> Redis -> Binance).
2. Normalize quantity to `stepSize`.
3. Validate `q_norm > 0` and `q_norm >= minQty`.
4. Compute notional and validate against `minNotional`.
5. If invalid, return rejection + suggested minimum valid quantity.
6. If valid, place order.
7. If Binance still rejects with filter error, refresh filters and store failure profile.

---

## 8) Implementation Notes for This Codebase

Relevant existing implementation already present in Executor:

- Fetches symbol filters from Binance exchange info.
- Applies floor rounding by `stepSize`.
- Rejects when quantity rounds to zero.
- Validates `MIN_NOTIONAL` when price is available.

Enhancements to implement next:

- Add Redis-backed symbol filter cache (shared across instances).
- Add fallback notional estimation for market orders when `request.Price == 0`.
- Store failure snapshots for `LOT_SIZE` and `MIN_NOTIONAL`.
- Add structured metric counters:
	- `executor_order_rejected_filter_total{symbol,rule}`
	- `executor_filter_cache_hit_total{layer=memory|redis|api}`

---

## 9) Acceptance Criteria

- No divide-by-zero or invalid arithmetic when `stepSize <= 0`.
- Orders are normalized to valid `stepSize` per symbol.
- `MIN_NOTIONAL` rejections are preempted whenever filter data is available.
- Suggested quantity is returned for invalid requests.
- Filter values are reused from Redis cache on subsequent orders.
- On restart, system can still validate without immediate exchangeInfo call (if Redis cache exists).

---

## 10) Example: Why 0.001 Fails

For a symbol with:

- `stepSize = 0.1`

Request:

- `q_req = 0.001`

Then:

$$
q_{norm} = \left\lfloor\frac{0.001}{0.1}\right\rfloor \times 0.1 = 0
$$

Result:

- Order must be rejected (or quantity must be increased to at least one step).

---

## 11) Final Recommendation

Treat exchange filters as first-class runtime configuration per symbol.

- Fetch from Binance reliably.
- Cache in memory + Redis.
- Validate before order submission.
- Learn from rejections and provide next valid quantity.

This prevents repeated `LOT_SIZE` and `MIN_NOTIONAL` failures and improves execution reliability in live trading.

---

## 12) UI Setting: Minimum and Maximum Amount Per Order

Business requirement:

- Add UI fields so admins can configure:
	- minimum order amount in quote currency (for example USDT),
	- maximum order amount in quote currency.
- These values must be validated on every order request.
- If either value is `null`, return an explicit error (do not place order).

Suggested UI fields:

- `MinOrderAmount` (decimal, required)
- `MaxOrderAmount` (decimal, required)

Validation in UI:

- both fields are required,
- both must be `> 0`,
- `MinOrderAmount <= MaxOrderAmount`.

---

## 13) DB Design (Reuse Existing system_settings Pattern)

This codebase already stores runtime settings in `public.system_settings` (key-value).

Recommended keys:

- `risk.minOrderAmount`
- `risk.maxOrderAmount`
- `risk.orderAmount.updatedBy`
- `risk.orderAmount.updatedAtUtc`

Why this design:

- consistent with existing `risk.*` setting model,
- no new table required,
- fast to integrate with current `SystemSettingsRepository`.

Optional dedicated-table alternative (only if strict schema needed):

- table `public.order_amount_limits` with columns:
	- `id` (PK),
	- `min_order_amount` NUMERIC(24,8) NOT NULL,
	- `max_order_amount` NUMERIC(24,8) NOT NULL,
	- `updated_by` TEXT NULL,
	- `updated_at_utc` TIMESTAMPTZ NOT NULL.

For current architecture, key-value in `system_settings` is the preferred option.

---

## 14) API Design

### Gateway Settings API (for UI)

1. `GET /api/settings/order-amount-limit`

Response:

```json
{
	"minOrderAmount": 5.0,
	"maxOrderAmount": 1000.0,
	"updatedBy": "admin",
	"updatedAtUtc": "2026-04-04T00:00:00Z"
}
```

2. `PUT /api/settings/order-amount-limit`

Request:

```json
{
	"minOrderAmount": 5.0,
	"maxOrderAmount": 1000.0,
	"updatedBy": "admin"
}
```

Validation:

- if `minOrderAmount` is `null` -> `400 BadRequest`: `minOrderAmount is required`
- if `maxOrderAmount` is `null` -> `400 BadRequest`: `maxOrderAmount is required`
- if `minOrderAmount <= 0` or `maxOrderAmount <= 0` -> `400 BadRequest`
- if `minOrderAmount > maxOrderAmount` -> `400 BadRequest`

On successful save:

- persist values in DB (`system_settings`),
- push config to Executor runtime endpoint (same push pattern used by risk/trading settings).

### Executor Runtime API (for hot reload)

3. `POST /api/trading/reload-order-amount-limit`

Request:

```json
{
	"minOrderAmount": 5.0,
	"maxOrderAmount": 1000.0
}
```

Validation:

- reject if any value is `null`,
- reject if invalid range,
- update in-memory config atomically.

---

## 15) Order-Time Validation Rule

For each order in Executor, after quantity normalization and price resolution:

$$
orderAmount = quantity \times effectivePrice
$$

Check in this order:

1. If `minOrderAmount` is `null` -> reject with configuration error.
2. If `maxOrderAmount` is `null` -> reject with configuration error.
3. If `orderAmount < minOrderAmount` -> reject with actionable message.
4. If `orderAmount > maxOrderAmount` -> reject with actionable message.
5. Continue with exchange validations (`LOT_SIZE`, `MIN_NOTIONAL`, etc.).

Suggested error messages:

- `Order amount limits are not configured: minOrderAmount is null`
- `Order amount limits are not configured: maxOrderAmount is null`
- `Order amount 3.20 is below configured minimum 5.00`
- `Order amount 1500.00 exceeds configured maximum 1000.00`

---

## 16) Redis Runtime Cache (Optional but Recommended)

To avoid DB read on every order, cache latest limits in Redis:

- key: `risk:order_amount_limit`

Payload:

```json
{
	"minOrderAmount": "5.0",
	"maxOrderAmount": "1000.0",
	"updatedAtUtc": "2026-04-04T00:00:00Z"
}
```

Load priority for Executor:

1. In-memory snapshot,
2. Redis,
3. DB fallback.

If all sources missing or any value null:

- fail closed (reject order),
- log configuration error with high severity.

---

## 17) Acceptance Criteria for New Requirement

- UI can save min/max order amount settings.
- DB persists both values successfully.
- Executor validates these values on every order.
- If either value is null, order is rejected with clear error.
- Gateway returns `400` for invalid/null payloads.
- Hot-reload updates runtime behavior without service restart.
