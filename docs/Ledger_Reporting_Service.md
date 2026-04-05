# System Specification Document: Ledger & Reporting Service

## 1. System Overview
The system is an independent Microservice within the broader architecture of the Automated Binance Futures Trading Bot. Its core responsibilities include managing cash flows (for both Mainnet and Testnet environments), calculating Profit and Loss (PnL) with exact precision, tracking trading fees, and providing a "Test Sessions" feature to facilitate backtesting and forward testing of trading algorithms.

**Bounded Context:**
* **In Scope (Responsibilities):** Ledger entry recording, Binance API data reconciliation, exact PnL calculation, statistical reporting, and test session reset management.
* **Out of Scope (Non-Responsibilities):** Trading decision making, order execution, and managing direct websocket connections to the exchange for active trading.

## 2. Technology Stack
* **Backend:** .NET (Web API & Background/Worker Services).
* **Frontend/Dashboard:** ReactJS.
* **Database:** PostgreSQL (Ensuring ACID properties for financial data integrity).
* **Real-time Communication:** SignalR (Pushing real-time PnL updates to the UI).
* **Inter-service Communication:** Message Broker (RabbitMQ/Kafka) for asynchronous event-driven flows.

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

## 5. Core Workflows

### 5.1. Data Ingestion & Reconciliation Sync
1.  A **.NET Worker Service** runs in the background, periodically calling the Binance API: `GET /fapi/v1/income`.
2.  The system inspects each returned record using the `tranId` (mapped to `BinanceTransactionId` in the DB).
3.  If it does not exist: Map the Binance income type to the system's `LedgerEntries.Type`.
4.  Identify the currently `ACTIVE` `SessionId` for the corresponding `AccountId`.
5.  Execute an `INSERT` into the `LedgerEntries` table.
6.  (Optional) Trigger a SignalR event to update the ReactJS UI in real-time.

### 5.2. Session Reset Workflow
This feature safely resets the testing environment while archiving historical data for future benchmarking, avoiding destructive hard deletes.
1.  Receive an HTTP POST request to `/api/v1/ledger/reset-session` with the payload: `AccountId`, `NewInitialBalance`, `AlgorithmName`.
2.  Update the old `TestSessions` record (currently ACTIVE): set `EndTime = NOW()` and `Status = ARCHIVED`.
3.  Create a new `TestSessions` record: set `Status = ACTIVE`, `InitialBalance = {NewInitialBalance}`.
4.  Insert an initialization log into `LedgerEntries`: `SessionId = {New_Id}`, `Type = INITIAL_FUNDING`, `Amount = {NewInitialBalance}`.
5.  Publish a `SessionResetEvent` message to the Message Broker so the main Trading Engine can update its context.

## 6. Crucial Notes for AI Coding Assistants
* **Idempotency:** All APIs and Event Handlers processing financial records MUST be idempotent. The `BinanceTransactionId` column with a Unique Constraint is the final safeguard at the database level.
* **Data Types:** Always use the `decimal` (or `numeric` in PostgreSQL) data type for all currency-related columns (`Amount`, `Balance`) to prevent floating-point precision loss.
* **Testnet/Mainnet Parity:** The business logic for Testnet and Mainnet is identical. The only differences are the Binance API Base URL and the `Environment` flag in the database.