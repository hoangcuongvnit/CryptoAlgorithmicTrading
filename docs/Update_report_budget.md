# Virtual Budget and Cash Flow Tracking System
## Enhancement Plan for Paper Trading Mode

---

## Executive Summary

The current paper trading reporting system lacks cash flow visibility and budget tracking. Reports show only trade counts and realized PnL, but **do not track**:
- Initial capital allocation
- Cash balance changes across sessions
- Profit/loss as percentage of budget
- Deposit/withdrawal transactions
- Equity curve (total account value over time)

This document provides a complete solution to add budget management and cash flow tracking to the system.

---

## 1) Problem Definition

### Current Limitations

1. **No Capital Tracking**: System doesn't track starting capital (e.g., $500) or current balance
2. **No Budget History**: Can't see cash flow changes across trading sessions
3. **Limited Visibility**: Reports show trades but don't connect them to capital impact
4. **No Capital Management**: Can't adjust virtual capital for subsequent test runs
5. **Incomplete Metrics**: Missing key metrics like:
   - Current available cash balance
   - Total equity (cash + open positions value)
   - Return on Investment (ROI) %
   - Drawdown from starting capital
   - Session-by-session capital allocation

### Impact on Testing

- Testers can't verify if system is profitable/sustainable
- Can't simulate capital constraints (e.g., "run with limited cash to test margin management")
- Can't correlate trading decisions with capital impact
- Difficult to perform "what-if" analysis with different starting capitals

---

## 2) Proposed Solution Architecture

### 2.1) Core Concept: Trading Capital Ledger

Track virtual capital through a **ledger-based system** that records:
- Opening balance per session
- Realized PnL per session (from closed trades)
- Capital adjustments (deposits/withdrawals for testing)
- Closing balance per session
- Equity curve (total account value = cash + open position market value)

### 2.2) Key Features

#### A. Budget Management
- Set initial virtual capital (default: $10,000 USDT for paper mode)
- Separate paper trading vs live trading budgets
- Support multiple test scenarios by resetting capital

#### B. Cash Flow Tracking
- Track cash balance changes per session
- Show running total equity across time
- Display unrealized vs realized PnL separately

#### C. Capital Operations
- **Deposit**: Add virtual capital (e.g., "add $500 for next phase of testing")
- **Withdraw**: Remove capital (e.g., "extract profits to reset for clean test")
- **Reset**: Clear all history and start with fresh budget
- **Snapshot**: Record point-in-time capital state for comparison

#### D. Metrics & KPIs
```
Capital Metrics:
- Initial Capital: Starting amount
- Current Equity: Total account value
- Available Cash: Unallocated capital
- Realized PnL: Profit/loss from closed trades
- Unrealized PnL: Profit/loss from open positions
- Total PnL: Realized + Unrealized
- ROI %: Total PnL / Initial Capital * 100
- Max Drawdown %: (Peak Equity - Trough) / Peak Equity * 100
- Equity Change %: (Current Equity - Initial Capital) / Initial Capital * 100

Session Metrics:
- Session Opening Balance
- Session Closing Balance
- Session PnL
- Session Equity Change %
```

---

## 3) Database Schema Enhancements

### 3.1) New Table: `paper_trading_ledger`

Tracks all capital transactions and balances for paper trading.

```sql
CREATE TABLE IF NOT EXISTS paper_trading_ledger (
    -- Primary Key
    id                  UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    
    -- Timestamp
    recorded_at_utc     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    -- Entry Reference
    reference_type      VARCHAR(30) NOT NULL  -- 'INITIAL', 'SESSION_PNL', 'DEPOSIT', 'WITHDRAW', 'RESET'
    reference_id        TEXT,                 -- session_id, transaction_id, etc.
    
    -- Balance Tracking
    cash_balance_before NUMERIC     NOT NULL,  -- Balance before this transaction
    cash_balance_after  NUMERIC     NOT NULL,  -- Balance after this transaction
    adjustment_amount   NUMERIC     NOT NULL,  -- Amount added/subtracted (positive=in, negative=out)
    
    -- Details
    description         TEXT,
    created_by          VARCHAR(100),         -- 'System', 'AUTOMATED/session_id', 'USER/username'
    
    -- Metadata
    is_paper            BOOLEAN     NOT NULL DEFAULT TRUE,
    currency            VARCHAR(5)  NOT NULL DEFAULT 'USDT'
);

CREATE INDEX idx_paper_trading_ledger_recorded_at ON paper_trading_ledger (recorded_at_utc DESC);
CREATE INDEX idx_paper_trading_ledger_type ON paper_trading_ledger (reference_type);
CREATE INDEX idx_paper_trading_ledger_session ON paper_trading_ledger (reference_id);
```

### 3.2) New Table: `session_capital_snapshot`

Snapshot capital state at session boundaries.

```sql
CREATE TABLE IF NOT EXISTS session_capital_snapshot (
    -- Primary Key
    id                          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    
    -- Session Identity
    session_id                  VARCHAR(20) NOT NULL UNIQUE,  -- e.g., '20260322-S1'
    session_date                DATE        NOT NULL,
    session_number              INT         NOT NULL,
    
    -- Capital Snapshot
    opening_cash_balance        NUMERIC     NOT NULL,
    closing_cash_balance        NUMERIC     NOT NULL,
    session_realized_pnl        NUMERIC     NOT NULL DEFAULT 0,
    
    -- Equity Snapshot (at session end)
    closing_equity_total        NUMERIC     NOT NULL,
    closing_holdings_value      NUMERIC     NOT NULL DEFAULT 0,
    
    -- Metrics
    session_equity_change_pct   NUMERIC,
    session_roi_pct             NUMERIC,
    
    -- Status
    is_flat_at_close            BOOLEAN     NOT NULL DEFAULT FALSE,
    is_complete                 BOOLEAN     NOT NULL DEFAULT FALSE,
    
    -- Timestamps
    session_start_utc           TIMESTAMPTZ NOT NULL,
    session_end_utc             TIMESTAMPTZ NOT NULL,
    recorded_at_utc             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    -- Metadata
    is_paper                    BOOLEAN     NOT NULL DEFAULT TRUE
);

CREATE INDEX idx_session_capital_snapshot_session_id ON session_capital_snapshot (session_id);
CREATE INDEX idx_session_capital_snapshot_date ON session_capital_snapshot (session_date DESC);
CREATE INDEX idx_session_capital_snapshot_complete ON session_capital_snapshot (is_complete);
```

### 3.3) Modify Existing `account_balance` Table

Current simple structure needs enhancement:

```sql
-- Rename to track paper trading specifically
ALTER TABLE account_balance RENAME TO paper_trading_account;

-- Add tracking fields
ALTER TABLE paper_trading_account ADD COLUMN IF NOT EXISTS 
    initial_balance     NUMERIC     DEFAULT 10000.00,  -- Starting capital
    current_equity      NUMERIC,                        -- Cash + holdings value
    updated_at_utc      TIMESTAMPTZ DEFAULT NOW(),
    is_active           BOOLEAN     DEFAULT TRUE;

-- Index for latest state query
CREATE INDEX idx_paper_trading_account_active ON paper_trading_account (is_active) WHERE is_active = TRUE;
```

### 3.4) Extend `orders` Table (Already Exists)

Add session capital tracking columns:

```sql
-- These fields may already exist, verify and add if missing:
ALTER TABLE orders ADD COLUMN IF NOT EXISTS 
    session_id          VARCHAR(20),              -- e.g., '20260322-S1' for session grouping
    session_phase       VARCHAR(20),              -- 'TRADING', 'LIQUIDATION', 'CLOSED'
    
    -- Capital impact snapshot (recorded at order entry)
    cash_available_at_execution NUMERIC,          -- Cash balance when order was executed
    total_equity_at_execution   NUMERIC;          -- Total equity value at order execution

CREATE INDEX idx_orders_session_id ON orders (session_id) WHERE session_id IS NOT NULL;
```

---

## 4) API Endpoints (New & Modified)

### 4.1) Budget Management Endpoints

#### GET `/api/trading/budget/status`
Returns current budget status for paper trading.

```json
Response {
  "mode": "paper|live",
  "initialCapital": 10000.00,
  "currentCashBalance": 9523.45,
  "totalHoldingsValue": 150.22,
  "totalEquity": 9673.67,
  "totalRealizedPnL": -326.33,
  "totalUnrealizedPnL": -0.00,
  "totalPnL": -326.33,
  "roiPercent": -3.26,
  "maxDrawdownPercent": -5.12,
  "lastUpdatedUtc": "2026-03-22T14:30:00Z",
  "activePositions": 2,
  "totalActiveSessions": 6
}
```

#### POST `/api/trading/budget/deposit`
Add virtual capital (for testing).

```json
Request {
  "amount": 500.00,
  "description": "Extended testing capital",
  "requestedBy": "tester@example.com"
}

Response {
  "success": true,
  "transactionId": "uuid",
  "newBalance": 10173.67,
  "recordedAt": "2026-03-22T15:00:00Z"
}
```

#### POST `/api/trading/budget/withdraw`
Remove virtual capital.

```json
Request {
  "amount": 200.00,
  "description": "Test complete - extract profits",
  "requestedBy": "tester@example.com"
}

Response {
  "success": true,
  "transactionId": "uuid",
  "newBalance": 9973.67,
  "recordedAt": "2026-03-22T15:05:00Z"
}
```

#### POST `/api/trading/budget/reset`
Reset to initial capital, clear all transactions.

```json
Request {
  "newInitialCapital": 5000.00,
  "description": "Begin Phase 2 testing",
  "requestedBy": "tester@example.com"
}

Response {
  "success": true,
  "resetAt": "2026-03-22T16:00:00Z",
  "newBalance": 5000.00,
  "previousBalance": 9973.67,
  "transactionCount": 127  // Purged count
}
```

### 4.2) Cash Flow & Ledger Endpoints

#### GET `/api/trading/budget/ledger`
Get capital transaction history.

```json
Query: ?from=2026-03-20&to=2026-03-22&limit=100&offset=0

Response {
  "transactions": [
    {
      "id": "uuid",
      "recordedAtUtc": "2026-03-22T14:30:00Z",
      "type": "SESSION_PNL",
      "referenceId": "20260322-S1",
      "description": "Session S1 realized profit",
      "balanceBefore": 9573.45,
      "balanceAfter": 9723.67,
      "adjustmentAmount": 150.22,
      "createdBy": "AUTOMATED/20260322-S1"
    },
    {
      "id": "uuid",
      "recordedAtUtc": "2026-03-22T10:00:00Z",
      "type": "DEPOSIT",
      "referenceId": "deposit_manual_001",
      "description": "Test capital addition",
      "balanceBefore": 9423.45,
      "balanceAfter": 9573.45,
      "adjustmentAmount": 150.00,
      "createdBy": "USER/tester@example.com"
    }
  ],
  "totalCount": 245,
  "pageInfo": { "page": 1, "pageSize": 100 }
}
```

#### GET `/api/trading/budget/equity-curve`
Get equity value over time for charting.

```json
Query: ?from=2026-03-20&to=2026-03-22&interval=session|hourly

Response {
  "points": [
    {
      "timestamp": "2026-03-20T00:00:00Z",
      "cashBalance": 10000.00,
      "holdingsValue": 0.00,
      "totalEquity": 10000.00,
      "realizedPnL": 0.00,
      "unrealizedPnL": 0.00,
      "totalPnL": 0.00,
      "roiPercent": 0.00
    },
    {
      "timestamp": "2026-03-20T04:00:00Z",
      "cashBalance": 9850.00,
      "holdingsValue": 125.50,
      "totalEquity": 9975.50,
      "realizedPnL": -24.50,
      "unrealizedPnL": 0.00,
      "totalPnL": -24.50,
      "roiPercent": -0.245
    }
  ]
}
```

### 4.3) Enhanced Session Report Endpoints

#### GET `/api/trading/report/sessions/daily` (MODIFIED)
Enhanced to include capital snapshots per session.

```json
Response {
  "date": "2026-03-22",
  "sessions": [
    {
      "sessionId": "20260322-S1",
      "sessionNumber": 1,
      "startTimeUtc": "2026-03-22T00:00:00Z",
      "endTimeUtc": "2026-03-22T04:00:00Z",
      
      // Existing fields
      "totalOrders": 5,
      "rejectedCount": 0,
      "winTrades": 3,
      "lossTrades": 2,
      "realizedPnL": 125.50,
      "isFlatAtClose": true,
      
      // NEW: Capital tracking
      "capitalSnapshot": {
        "openingCashBalance": 10000.00,
        "closingCashBalance": 9850.00,
        "sessionRealizedPnL": 125.50,
        "closingEquityTotal": 9975.50,
        "closingHoldingsValue": 125.50,
        "sessionEquityChangePct": -0.245,
        "sessionROIPct": 1.255
      }
    }
  ],
  "dailySummary": {
    "dayOpeningBalance": 10000.00,
    "dayClosingBalance": 9850.00,
    "dayRealizedPnL": 125.50,
    "dayTotalEquity": 9975.50,
    "dayEquityChangePct": -0.245,
    "dayROIPct": 1.255,
    "flatClosedSessions": 6,
    "totalDaylyPnL": 125.50
  }
}
```

#### GET `/api/trading/report/sessions/{sessionId}/capital`
Detailed capital breakdown for a single session.

```json
Response {
  "sessionId": "20260322-S1",
  "period": {
    "startUtc": "2026-03-22T00:00:00Z",
    "endUtc": "2026-03-22T04:00:00Z"
  },
  "capital": {
    "openingCashBalance": 10000.00,
    "closingCashBalance": 9850.00,
    "openingFloatingEquity": 0.00,
    "closingFloatingEquity": 125.50,
    "sessionPnL": 125.50,
    "realizedPnL": 125.50,
    "unrealizedPnL": 0.00,
    "fees": 24.50,
    "netPnL": 101.00
  },
  "riskMetrics": {
    "maxCapitalUsed": 2500.00,
    "percentOfBudgetUsed": 25.0,
    "minCashReached": 7500.00,
    "flatAtSessionEnd": true
  }
}
```

---

## 5) Frontend Report Enhancements

### 5.1) New Dashboard Widget: Budget Overview Card

Display on main trading page:

```
╔════════════════════════════════════════════════╗
║         VIRTUAL TRADING BUDGET                  ║
├════════════════════════════════════════════════┤
│                                                │
│  Initial Capital:        $10,000 USDT         │
│  Current Balance:         $9,850 USDT         │  
│  Total Equity:            $9,975.50 USDT      │
│                                                │
│  Total PnL:              -$24.50  (-0.25%)   │
│    ├─ Realized:          +$125.50             │
│    └─ Unrealized:         -$150.00            │
│                                                │
│  Max Drawdown:            -5.12%              │
│  Current ROI:             -0.25%              │
│                                                │
│  [Deposit +] [Withdraw -] [Reset] [History]   │
│                                                │
└════════════════════════════════════════════════┘
```

### 5.2) Cash Flow Tab in Session Report Page

New section showing:
- Opening/closing balance per session
- Equity curve chart (line graph)
- Capital allocation pie chart
- Ledger table (deposits, withdrawals, session PnL)

### 5.3) Budget Management Modal

Pop-up dialog with forms for:
- **Deposit**: Input amount, description, submit
- **Withdraw**: Input amount, approval confirmation
- **Reset**: Confirm reset, set new initial capital
- **History**: Tabular view of all transactions

### 5.4) Summary Report Section: "Budget Impact"

Add to daily report page:

```
Daily Budget Summary:
- Sessions: 6/6 completed
- Net Daily PnL: +$125.50 (1.26%)
- Best Session: S1 +$150.00
- Worst Session: S4 -$25.50
- Equity Change: $10,000 → $10,125.50
- Current Drawdown: 0.00%
```

---

## 6) Implementation Phases

### Phase 1: Database & Core (Priority: HIGH)
**Timeline: 1-2 weeks**

Deliverables:
- Create `paper_trading_ledger` table
- Create `session_capital_snapshot` table
- Augment `orders` table with session capital fields
- Initialize ledger with current account_balance as INITIAL entry

Tasks:
1. Write migration scripts: `add-capital-ledger-tables.sql`
2. Add repository methods for ledger inserts/queries
3. Create audit trail for all capital changes
4. Write integration tests

### Phase 2: Backend APIs (Priority: HIGH)
**Timeline: 1-2 weeks**

Deliverables:
- Budget status endpoint (`GET /api/trading/budget/status`)
- Deposit/Withdraw/Reset endpoints
- Ledger history endpoint
- Enhanced session report with capital data

Tasks:
1. Add `CapitalManagementService` class
2. Implement `CapitalLedgerRepository` queries
3. Add `POST /api/trading/budget/*` endpoints
4. Modify session report aggregation to include capital snapshots
5. Add integration tests for workflow scenarios

### Phase 3: Session Integration (Priority: MEDIUM)
**Timeline: 1 week**

Deliverables:
- Automatic capital snapshot at session boundaries
- Real-time equity calculation
- Session closure with capital reconciliation

Tasks:
1. Extend `PositionTracker` to calculate total market value
2. Create session-end event handler to record capital snapshot
3. Implement equity curve calculation service
4. Add unit tests for equity calculations

### Phase 4: Frontend UI (Priority: MEDIUM)
**Timeline: 2 weeks**

Deliverables:
- Budget Overview Card
- Cash Flow tab in reports
- Budget Management Modal
- Equity curve chart

Tasks:
1. Create React component for budget overview
2. Create deposit/withdraw/reset forms
3. Build ledger history table
4. Add equity curve chart (using Chart.js or Recharts)
5. Integrate with hooks (useSessionDailyReport, new useBudgetStatus)

### Phase 5: Testing & Documentation (Priority: HIGH)
**Timeline: 1 week**

Deliverables:
- E2E test scenarios (deposit → trade → withdraw)
- Load tests for ledger queries
- User guide for budget features
- API documentation updates

Tasks:
1. Create test scenarios in test runner
2. Performance test ledger queries with large histories
3. Write user guide section
4. Update API documentation
5. Create demo video

---

## 7) Data Consistency & Reconciliation

### 7.1) Audit Reconciliation Logic

Every session end, verify:
```
Expected Equity = Previous Session Equity + Session PnL
Actual Equity = Current Cash + Sum(Open Positions Market Value)

If Actual != Expected:
  → Log reconciliation error
  → Flag for manual review
  → Report discrepancy in UI
```

### 7.2) Periodic Validation Job

HouseKeeper service task:
- Daily: Verify all sessions have capital snapshots
- Daily: Reconcile ledger sum against reported balances
- Weekly: Archive old ledger entries (> 30 days in paper mode)
- Weekly: Alert if equity calculations drift from orders data

---

## 8) Security & Permissions

### 8.1) Access Control

- **Budget queries**: Any authenticated user can view
- **Deposit/Withdraw/Reset**: Only authorized users (admin, specified test runners)
- **Ledger access**: Audit all capital operations in activity log

### 8.2) Validation Rules

- Withdraw amount: Cannot exceed current balance (prevent overdraft)
- Deposit amount: Max 1M USDT per transaction (sanity check)
- Reset: Requires confirmation + audit log entry
- Rate limiting: Max 10 transactions per minute per user

---

## 9) Example Workflow: Complete Testing Scenario

### Day 1: Initial Setup
1. System initializes with $10,000 USDT paper trading budget
2. Ledger records: `INITIAL | balance_before: $0 | balance_after: $10,000`

### Day 1 Sessions
1. **Session S1 (00:00-04:00)**: Trade $500 worth, realize +$50 profit → ledger records `SESSION_PNL`
2. **Session S2 (04:00-08:00)**: Lose -$25 → ledger records `SESSION_PNL`
3. Equity curve now shows: $10,000 → $10,025 (net +$25)

### Day 2: Mid-Test Adjustment
1. Tester decides to test with more capital
2. Calls `POST /api/trading/budget/deposit` with $500
3. Ledger records: `DEPOSIT | balance_before: $10,025 | balance_after: $10,525`
4. Report shows new baseline for ROI calculations

### Day 3: Test Complete
1. After 6 more sessions, total PnL is +$175
2. Final equity: $10,700
3. Tester calls `POST /api/trading/budget/reset` with new_capital=$5,000
4. **New** ledger created for Phase 2 testing
5. Previous ledger archived and can be exported for analysis

### Reporting Output
```
Daily Report for 2026-03-22:

Budget Summary:
  Starting Equity:     $10,000
  Ending Equity:       $10,525
  Total PnL:           +$525 (5.25%)
  
Session Breakdown:
  S1: Open $10,000  → Close $10,050  (+$50)
  S2: Open $10,050  → Close $10,025  (-$25)
  S3: Open $10,025  → Close $10,150  (+$125)
  S4: Open $10,150  → Close $10,100  (-$50)
  S5: Open $10,100  → Close $10,250  (+$150)
  S6: Open $10,250  → Close $10,525  (+$275)  ← Best session
  
Max Drawdown: -0.5% (from S4 dip)
ROI: 5.25%
```

---

## 10) Success Criteria

### Primary Metrics
- ✅ Budget balance visible and updating in real-time
- ✅ Can deposit/withdraw virtual capital
- ✅ Session capital snapshots reconcile to order PnLs
- ✅ Equity curve chart shows accurate progression
- ✅ All test scenarios pass without data corruption

### Performance Criteria
- ✅ Budget endpoint responds < 100ms
- ✅ Ledger query with 10k rows completes < 500ms
- ✅ Equity curve calculation for 30-day period < 1s
- ✅ API handle 1000 req/s concurrent load

### User Experience
- ✅ Budget card displayed on homepage
- ✅ Deposit/Withdraw flows < 3 clicks
- ✅ Historical data searchable and exportable
- ✅ Clear error messages on invalid operations

---

## 11) Error Handling & Edge Cases

### Common Scenarios

| Scenario | Handling |
|----------|----------|
| Withdraw more than available | Return 400 error: "Insufficient balance" |
| Order lands in wrong session | Assign to correct session by timestamp, note discrepancy |
| Session PnL calculation drifts | Flag reconciliation error, audit log created |
| Ledger insert fails | Retry with exponential backoff, alert admin |
| User depletes capital to $0 | Allow (for stress testing), display warning |
| Reset during active session | Prevent until session closes or manually override |

---

## 12) Integration Timeline

```
Week 1-2:   Database design + migration scripts
Week 3-4:   Backend APIs + core service
Week 5:     Session integration + auto snapshots
Week 6-7:   Frontend UI components
Week 8:     Testing, E2E scenarios, optimization
Week 9:     Documentation + user guide
Week 10:    UAT + production rollout
```

---

## 13) Future Enhancements (Not in Phase 1)

- Multi-currency support (simulate trading in different fiat)
- Leverage/margin simulation (virtual credit line)
- Performance benchmarking (compare vs market indices)
- Budget alerts (low balance warnings)
- Capital forecast (project equity based on strategy backtest)
- Export to CSV/Excel with full audit trail

---

## Appendix A: SQL Migration Script Template

```sql
-- Migration: add-capital-ledger-tables.sql
-- Purpose: Add virtual budget and cash flow tracking for paper trading
-- Date: 2026-03-22

BEGIN TRANSACTION;

-- 1. Create ledger table
CREATE TABLE IF NOT EXISTS paper_trading_ledger (...)

-- 2. Create session snapshot table
CREATE TABLE IF NOT EXISTS session_capital_snapshot (...)

-- 3. Extend orders table
ALTER TABLE orders ADD COLUMN IF NOT EXISTS session_id VARCHAR(20);
ALTER TABLE orders ADD COLUMN IF NOT EXISTS session_phase VARCHAR(20);

-- 4. Initialize ledger with existing balance
INSERT INTO paper_trading_ledger 
  (recorded_at_utc, reference_type, cash_balance_before, cash_balance_after, adjustment_amount, description, created_by)
SELECT NOW(), 'INITIAL', 0, balance, balance, 'System initialization', 'SYSTEM'
FROM paper_trading_account
WHERE is_active = TRUE
LIMIT 1;

COMMIT;
```

---

## Appendix B: API Usage Examples

### Example 1: Check Current Budget
```bash
curl -X GET "http://localhost:5000/api/trading/budget/status" \
  -H "Authorization: Bearer $TOKEN"

# Returns current equity, cash, ROI, etc.
```

### Example 2: Make a Deposit
```bash
curl -X POST "http://localhost:5000/api/trading/budget/deposit" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "amount": 500.00,
    "description": "Extended testing phase",
    "requestedBy": "tester@example.com"
  }'
```

### Example 3: Get Equity Curve
```bash
curl -X GET "http://localhost:5000/api/trading/budget/equity-curve?from=2026-03-20&to=2026-03-22&interval=session" \
  -H "Authorization: Bearer $TOKEN"

# Returns array of equity snapshots over time
```

---

## Appendix C: Environment Variable Configuration

Add to `.env` for paper trading customization:

```
# Paper Trading Budget Settings
PAPER_TRADING_INITIAL_CAPITAL=10000.00
PAPER_TRADING_CURRENCY=USDT
PAPER_TRADING_ENABLE_DEPOSITS=true
PAPER_TRADING_ENABLE_WITHDRAWALS=true
PAPER_TRADING_MAX_DEPOSIT_AMOUNT=1000000.00
PAPER_TRADING_AUTO_SNAPSHOT_SESSIONS=true
PAPER_TRADING_RECONCILE_FREQUENCY=hourly
```

---

## Conclusion

This solution transforms the reporting system from a **trade-centric** view to a **capital-centric** view. Testers and traders can now:

1. ✅ See real-time cash flow and equity changes
2. ✅ Manage virtual capital for different test scenarios
3. ✅ Correlate trading decisions with financial impact
4. ✅ Gain confidence that the system is profit-tracking capable
5. ✅ Validate system behavior under different capital constraints

The implementation follows best practices for financial ledger systems with full audit trails, reconciliation checks, and permission-based access controls.
