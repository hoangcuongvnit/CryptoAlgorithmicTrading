# Investigation Report: SOLUSDT rejected with InvalidOrderParameters

## Issue observed

- Error from Executor:
	- `Order amount 0.0801565 is below configured minimum 5 for SOLUSDT`
- Result: order flow was blocked before execution.

## Root cause analysis

I traced the request path end-to-end:

1. Strategy maps a signal into an `OrderRequest` using a fixed base-asset quantity:
	 - `Trading:DefaultOrderQuantity = 0.001`
2. Executor validates order amount using **notional value in USDT**:
	 - `orderAmount = request.Quantity * effectivePrice`
	 - rejects when `< MinOrderAmount` (default `5`)
3. For SOL around ~80 USDT, `0.001 * 80 = 0.08 USDT`, so it is rejected correctly by current validation logic.

So this is a **business-rule conflict** between:

- Strategy sizing rule (fixed coin quantity)
- Executor amount-limit rule (minimum quote notional in USDT)

The system components were individually correct, but inconsistent with each other.

## Code updates implemented

To align business logic and avoid these rejections, I updated Strategy sizing to be notional-aware.

### 1. Added notional sizing settings

File: `src/Services/Strategy/Strategy.Worker/Configuration/Settings.cs`

- Added `DefaultOrderNotionalUsdt` (default `25`)
- Added `MinOrderNotionalUsdt` (default `5`)

### 2. Updated mapper sizing logic

File: `src/Services/Strategy/Strategy.Worker/Services/SignalToOrderMapper.cs`

- Replaced direct use of `DefaultOrderQuantity` with `ResolveOrderQuantity(entryPrice)`
- New logic:
	- If `DefaultOrderNotionalUsdt > 0`, quantity is computed as `DefaultOrderNotionalUsdt / entryPrice`
	- Enforces floor by `MinOrderNotionalUsdt / entryPrice`
	- Rounds quantity to 8 decimals

This ensures generated orders are consistent with Executor's minimum notional validation.

### 3. Added config values in Strategy appsettings

File: `src/Services/Strategy/Strategy.Worker/appsettings.json`

- `DefaultOrderNotionalUsdt: 25`
- `MinOrderNotionalUsdt: 5`

## Expected behavior after fix

- SOLUSDT and other lower-priced symbols are no longer blocked due to tiny fixed quantity.
- Strategy-generated entry orders are aligned with Executor notional limits.
- Existing risk checks in RiskGuard still apply as before.

## Operational note

If you change order amount limits in Gateway/Executor later, keep `Strategy:MinOrderNotionalUsdt` aligned with that minimum to avoid future conflicts.
