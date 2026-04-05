# System Specification Document: Ledger & Reporting Service

## 1. System Overview
The system is an independent Microservice within the broader architecture of the Automated Binance Futures Trading Bot. Its core responsibilities include managing cash flows (for both Mainnet and Testnet environments), calculating Profit and Loss (PnL) with exact precision, tracking trading fees, and providing a "Test Sessions" feature to facilitate backtesting and forward testing of trading algorithms.

Mode policy: The service supports both live environments (Mainnet and Testnet APIs) and does not include paper trading mode.

**Bounded Context:**
* **In Scope (Responsibilities):** Ledger entry recording, Binance API data reconciliation, exact PnL calculation, statistical reporting, and test session reset management.
* **Out of Scope (Non-Responsibilities):** Trading decision making, order execution, and managing direct websocket connections to the exchange for active trading.

## 2. Technology Stack
* **Backend:** .NET (Web API & Background/Worker Services).
* **Frontend/Dashboard:** ReactJS.
* **Database:** PostgreSQL (Ensuring ACID properties for financial data integrity).
* **Real-time Communication:** SignalR (Pushing live realized/unrealized PnL and equity updates to the UI).
* **Inter-service Communication:** Reliable Message Broker using Redis Streams (consumer groups) or RabbitMQ via MassTransit (durable queues, retries, dead-letter).

## 3. Database Schema
The system implements an **Immutable Ledger** pattern. Financial records are strictly `INSERT`-only; direct `UPDATE` operations on existing financial records are prohibited to prevent race conditions and ensure auditability.

### 3.1. `VirtualAccounts` Table
Stores virtual account details mapped to physical/testnet exchange accounts.
* `Id` (PK, UUID)
* `Environment` (Enum: MAINNET, TESTNET)
* `BaseCurrency` (VARCHAR, e.g., USDT)
* `CreatedAt` (Timestamp)

### 3.2. `TestSessions` Table
Manages the lifecycles of trading algorithms, directly supporting the "Reset" functionality.
* `Id` (PK, UUID)
* `AccountId` (FK -> VirtualAccounts.Id)
* `AlgorithmName` (VARCHAR, Name or version of the strategy)
* `InitialBalance` (DECIMAL, The starting capital for this specific session)
* `StartTime` (Timestamp)
* `EndTime` (Timestamp, Nullable)
* `Status` (Enum: ACTIVE, ARCHIVED)

### 3.3. `LedgerEntries` Table
The core ledger table that records every cash flow fluctuation.
* `Id` (PK, UUID)
* `SessionId` (FK -> TestSessions.Id)
* `BinanceTransactionId` (VARCHAR, Unique Index - Crucial for idempotency to prevent duplicate events from Binance)
* `Type` (Enum: INITIAL_FUNDING, REALIZED_PNL, COMMISSION, FUNDING_FEE, WITHDRAWAL)
* `Amount` (DECIMAL, Positive value = inflow, Negative value = outflow)
* `Symbol` (VARCHAR, Nullable, e.g., BTCUSDT)
* `Timestamp` (Timestamp, The actual execution time from Binance)

## 4. Mathematical Formulas

The system uses the following formulas for internal calculations and API responses.

**A. Current Balance:**
The total balance of a specific test session is the sum of all ledger entries associated with that session.
$$CurrentBalance = \sum_{i=1}^{n} LedgerEntries.Amount \quad \text{(where SessionId = X)}$$

**B. Net Profit and Loss (Net PnL):**
Grouped by `Symbol` or the entire `SessionId`. *(Note: Commission is usually a negative value).*
$$Net\_PnL = Realized\_PnL + Commission + Funding\_Fee$$

**C. Return on Equity (ROE):**
$$ROE = \left( \frac{Net\_PnL}{Initial\_Margin\_Of\_Trade} \right) \times 100\%$$

**D. Real-time Equity:**
$$RealTimeEquity = CurrentBalance + UnrealizedPnL$$

## 5. Core Workflows

### 5.1. Data Ingestion & Reconciliation Sync
1.  The **Executor** publishes normalized accounting events to Redis Stream `ledger:events` immediately after successful trade execution.
2.  For close-side executions, Executor emits ledger-safe deltas (for example `REALIZED_PNL`, `COMMISSION`) instead of gross notional buy/sell values.
3.  FinancialLedger consumes `ledger:events` via consumer group with acknowledgement and retry semantics (at-least-once delivery).
4.  FinancialLedger applies idempotency using `BinanceTransactionId` or an equivalent unique transaction key from the stream event.
5.  FinancialLedger maps each event into immutable `LedgerEntries` inserts.
6.  FinancialLedger emits SignalR updates only after durable persistence succeeds.

### 5.2. Session Reset Workflow
This feature safely resets the testing environment while archiving historical data for future benchmarking, avoiding destructive hard deletes.
1.  Receive an HTTP POST request to `/api/ledger/sessions/reset` with payload: `AccountId`, `NewInitialBalance`, `AlgorithmName`.
2.  FinancialLedger checks current open positions from Executor.
3.  If open positions exist and request does not include explicit confirmation, API returns `409 Conflict` with `requiresConfirmation=true` and current `openPositions`.
4.  After user confirmation, FinancialLedger requests close-all from Executor and waits for operation completion by polling close-all status endpoint until success/failure/timeout.
5.  Only when close-all is confirmed successful does FinancialLedger archive the previous `TestSessions` row and create a new `ACTIVE` session.
6.  FinancialLedger inserts an `INITIAL_FUNDING` ledger entry for the new session and completes the reset workflow.

### 5.3. Real-time Unrealized PnL & Equity Workflow
1.  Trading Engine publishes live markPrice and open position snapshots.
2.  Ledger (or projection worker) computes unrealized PnL continuously.
3.  SignalR Hub streams `CurrentBalance`, `UnrealizedPnL`, and `RealTimeEquity` to frontend clients.
4.  Frontend renders real-time risk/equity state without calling Binance synchronization REST endpoints.

## 6. Crucial Notes for AI Coding Assistants
* **Idempotency:** All APIs and Event Handlers processing financial records MUST be idempotent. The `BinanceTransactionId` column with a Unique Constraint is the final safeguard at the database level.
* **Data Types:** Always use the `decimal` (or `numeric` in PostgreSQL) data type for all currency-related columns (`Amount`, `Balance`) to prevent floating-point precision loss.
* **Testnet/Mainnet Parity:** The business logic for Testnet and Mainnet is identical. The only differences are the Binance API Base URL and the `Environment` flag in the database.
* **Reliability Requirement:** Do not use fire-and-forget Pub/Sub for critical financial events; use durable streams/queues with acknowledgements and retries.
* **Synchronization Source:** Do not poll Binance `GET /fapi/v1/income` for synchronization; use Executor WebSocket-derived events to avoid API rate-limit pressure.