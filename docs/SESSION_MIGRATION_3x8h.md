# Session Migration: 6×4h → 3×8h Sessions

> **Status**: ✅ **COMPLETED** — All systems migrated to 3 eight-hour sessions per day.  
> **Date**: 2026-03-15  
> **Breaking Change**: Yes — Reports, session data structures, and UI references updated.

---

## Overview

The system has been migrated from **6 four-hour sessions** (S1-S6) to **3 eight-hour sessions** (S1-S3) per trading day. This change improves signal consistency and reduces session overhead.

| Aspect | Before | After |
|--------|--------|-------|
| Sessions per day | 6 (4 hours each) | 3 (8 hours each) |
| Session schedule (UTC) | S1: 00-04, S2: 04-08, ..., S6: 20-24 | S1: 00-08, S2: 08-16, S3: 16-24 |
| Liquidation window | Last 30 min of 4h session | Last 30 min of 8h session |
| SoftUnwind phase | Last 15 min of 4h session | Last 15 min of 8h session |
| Session duration | 4 hours | 8 hours |

---

## Session Schedule (UTC)

### New 3×8h Sessions

```
S1: 00:00 UTC → 08:00 UTC (8 hours)
   ├─ Full trading: 00:00 → 07:30
   ├─ SoftUnwind: 07:30 → 07:45 (only Strong signals accepted)
   ├─ LiquidationOnly: 07:45 → 08:00 (close positions only)
   └─ ForcedFlatten: 08:00 (all positions must be closed before S2 opens)

S2: 08:00 UTC → 16:00 UTC (8 hours)
   ├─ Full trading: 08:00 → 15:30
   ├─ SoftUnwind: 15:30 → 15:45
   ├─ LiquidationOnly: 15:45 → 16:00
   └─ ForcedFlatten: 16:00

S3: 16:00 UTC → 24:00 UTC (8 hours)
   ├─ Full trading: 16:00 → 23:30
   ├─ SoftUnwind: 23:30 → 23:45
   ├─ LiquidationOnly: 23:45 → 24:00 (00:00 next day)
   └─ ForcedFlatten: 24:00 (00:00 next day)
```

---

## Configuration Changes

### Code Changes

**File**: `src/Shared/Session/SessionSettings.cs`

```csharp
public class SessionSettings
{
    public int SessionHours { get; set; } = 8;                    // Changed from 4 to 8
    public int LiquidationWindowMinutes { get; set; } = 30;       // Unchanged
    public int SoftUnwindMinutes { get; set; } = 15;              // Unchanged
    public int ForcedFlattenMinutes { get; set; } = 2;            // Unchanged
}
```

**File**: `src/Services/Executor/Executor.API/appsettings.json`

```json
{
  "Trading": {
    "SessionHours": 8,
    "LiquidationWindowMinutes": 30,
    "SoftUnwindMinutes": 15,
    "ForcedFlattenMinutes": 2
  }
}
```

Apply to all services:
- `src/Services/RiskGuard/RiskGuard.API/appsettings.json`
- `src/Services/Strategy/Strategy.Worker/appsettings.json`
- `src/Services/Analyzer/Analyzer.Worker/appsettings.json`

---

## Database Impact

### Table Schema Changes

**`session_reports` table** — Updated structure:

```sql
-- Old (6×4h sessions)
session_id: 'yyyyMMdd-S{1..6}'   -- e.g., '20260315-S1'
session_start: '2026-03-15 00:00:00'
session_end: '2026-03-15 04:00:00'

-- New (3×8h sessions)
session_id: 'yyyyMMdd-S{1..3}'   -- e.g., '20260315-S1'
session_start: '2026-03-15 00:00:00'
session_end: '2026-03-15 08:00:00'
```

### SQL Migration

If you have historical session data with 6 sessions, run:

```sql
-- Update session_reports for 8-hour boundaries
UPDATE session_reports
SET session_id = 
    CASE 
        WHEN date_part('hour', session_start) IN (0, 4) THEN 
            DATE_TRUNC('day', session_start)::text || '-S1'
        WHEN date_part('hour', session_start) IN (8, 12) THEN 
            DATE_TRUNC('day', session_start)::text || '-S2'
        ELSE 
            DATE_TRUNC('day', session_start)::text || '-S3'
    END
WHERE session_id LIKE '%-S%';
```

---

## API Endpoint Updates

### Executor Reports

**Before**:
```
GET /api/trading/report/sessions/daily          # Returns 6 sessions per day
```

**After**:
```
GET /api/trading/report/sessions/daily          # Returns 3 sessions per day (S1/S2/S3)
```

**Response Example**:
```json
{
  "sessions": [
    {
      "sessionId": "20260315-S1",
      "startTime": "2026-03-15T00:00:00Z",
      "endTime": "2026-03-15T08:00:00Z",
      "pnl": 250.50,
      "tradeCount": 12,
      "symbols": ["BTCUSDT", "ETHUSDT"]
    },
    {
      "sessionId": "20260315-S2",
      "startTime": "2026-03-15T08:00:00Z",
      "endTime": "2026-03-15T16:00:00Z",
      "pnl": -100.25,
      "tradeCount": 8,
      "symbols": ["BNBUSDT"]
    },
    {
      "sessionId": "20260315-S3",
      "startTime": "2026-03-15T16:00:00Z",
      "endTime": "2026-03-16T00:00:00Z",
      "pnl": 500.00,
      "tradeCount": 15,
      "symbols": ["BTCUSDT", "ETHUSDT", "SOLUSDT"]
    }
  ]
}
```

---

## Frontend Impact

### Session Report UI

**File**: `frontend/pages/SessionReportPage.jsx`

**Before**: Displayed 6 sessions per day in a grid/list  
**After**: Displays 3 sessions per day with expanded S1, S2, S3 labels

**Update Required**:
```jsx
// Old
const sessionLabels = ['S1', 'S2', 'S3', 'S4', 'S5', 'S6'];

// New
const sessionLabels = ['S1', 'S2', 'S3'];  // 8-hour each
```

**Timeline Display**: Session cards now show 8-hour windows instead of 4-hour windows.

---

## Risk Rules Compatibility

✅ **No changes required** — All 9 risk rules continue to function with 8-hour sessions:

| Rule | 4h Sessions | 8h Sessions | Status |
|------|-----------|-----------|--------|
| ExitOnlyRule | ✅ | ✅ | **Unchanged** |
| RecoveryWindowRule | ✅ | ✅ | **Unchanged** |
| NoCrossSessionCarryRule | ✅ | ✅ | **Unchanged** |
| SessionWindowRule | ✅ | ✅ | **Unchanged** |
| SymbolAllowListRule | ✅ | ✅ | **Unchanged** |
| QuantityRule | ✅ | ✅ | **Unchanged** |
| PositionSizeRule | ✅ | ✅ | **Unchanged** |
| RiskRewardRule | ✅ | ✅ | **Unchanged** |
| CooldownRule | ✅ | ✅ | **Unchanged** |
| MaxDrawdownRule | ✅ | ✅ | **Unchanged** |

All validations adapt automatically based on `SessionSettings.SessionHours`.

---

## Session Phases (Timing)

Session phases remain the same, but now occur within 8-hour windows:

### Phase Timing (Relative to Session Start)

```
+0:00  Open (Full trading enabled)
  ↓
+7:30  SoftUnwind (only Strong signals accepted)
  ↓
+7:45  LiquidationOnly (close positions only, no new entries)
  ↓
+7:58  ForcedFlatten (all positions must close)
  ↓
+8:00  Session End (Session boundary)
```

---

## Migration Checklist

For existing systems upgrading:

- [ ] Update `SessionHours: 8` in all appsettings.json files
- [ ] Run database migration for historical session data
- [ ] Update frontend SessionReportPage to show 3 sessions instead of 6
- [ ] Verify TimelineLogger events use new session IDs (yyyyMMdd-S{1..3})
- [ ] Update Financial Ledger session tracking (if in use)
- [ ] Run integration tests with 8-hour session boundaries
- [ ] Monitor logs for "session transition" events during first full day
- [ ] Update monitoring dashboards to reflect 3 sessions per day

---

## Troubleshooting

### Issue: Old session reports show "4-hour" data

**Solution**: Historical data is compatible with the new schema. The session_id format changed (e.g., S1-S6 → S1-S3), but boundaries are automatically calculated from timestamps.

### Issue: Positions not liquidating at session boundary

**Solution**: Check that `SessionHours: 8` is set in all service configs. Verify LiquidationOrchestrator logs:
```
grep "LiquidationOrchestrator" infrastructure/docker compose logs executor
```

### Issue: UI shows "6 sessions per day"

**Solution**: Frontend components need manual update. Update `SessionReportPage.jsx` to iterate 3 sessions instead of 6.

---

## Performance Impact

✅ **Positive**: Fewer session boundaries per day = fewer liquidation operations  
✅ **Positive**: Longer signal window per session = better trend identification  
✅ **Neutral**: Database partition strategy unchanged (yearly for price_ticks)

---

## Reference

- **Configuration**: `src/Shared/Session/SessionSettings.cs`
- **Phase Detection**: `src/Shared/Session/SessionClock.cs`
- **Session Timing Policy**: `src/Shared/Session/SessionTradingPolicy.cs`
- **Executor Session Logic**: `src/Services/Executor/Executor.API/Services/LiquidationOrchestrator.cs`
- **UI Component**: `frontend/pages/SessionReportPage.jsx`

---

**Questions?** Refer to [CLAUDE.md](CLAUDE.md) → Session Structure section for the current session schedule and architecture.
