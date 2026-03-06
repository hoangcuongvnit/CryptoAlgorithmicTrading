# Plan 2 — 12-Month Historical + Real-Time Data Collection from Binance

## Objectives

- Build a complete **12-month 1m OHLCV dataset** for robust backtesting across multiple market regimes.
- Maintain a **continuous real-time data stream** for forward testing and backtest comparison.
- Ensure the dataset is immediately usable by Analyzer and Strategy services.

## Scope for This Phase

- Initial symbols: `BTCUSDT`, `ETHUSDT`, `BNBUSDT`, `SOLUSDT`, `XRPUSDT`.
- Historical range: from **03/2025 to present** (last 12 months).
- Real-time feed: 1m Kline + mini ticker from Binance WebSocket.
- Centralized storage in PostgreSQL/TimescaleDB (`price_ticks`).

## Technical Decisions

1. **Primary historical source:** Binance Vision (`data.binance.vision`) monthly Kline files.
2. **Baseline resolution:** start with `1m` to stabilize and validate the algorithm.
3. **Higher-resolution data:** use `1s` only in short windows (latest 1 week) for scalping/slippage tuning.
4. **Idempotency rule:** re-imports must not create duplicates (upsert/unique key).
5. **Separated architecture:**
   - `DataIngestor`: real-time ingestion.
   - `HistoricalCollector` (new): backfill + gap filling.

## Detailed Execution Plan

### Step 1 — Complete Real-Time Persistence to DB

DataIngestor currently publishes to Redis. Add persistence into `price_ticks`.

- Create a batch DB writer repository (`Npgsql` + `Dapper` or copy-bulk).
- Flush every 3–5 seconds or when batch size is reached.
- Add unique constraint `(time, symbol, interval)` to prevent duplicates.
- Keep worker resilient under DB slowdown (buffer + retry + warning logs).

**Expected outcome:** real-time 1m candles continuously appear in `price_ticks`.

### Step 2 — Build HistoricalCollector Service

- Create a new worker to download monthly `.zip` CSV files from Binance Vision per symbol.
- Parse CSV Klines and map them into `price_ticks` schema.
- Write with large batches (5k–20k rows per write).
- Support safe stop/resume using symbol-month checkpoints.

**Expected outcome:** 12-month history is loaded in minutes to tens of minutes depending on network throughput.

### Step 3 — Run the 12-Month Backfill

Recommended execution order:

1. Start infrastructure (`redis`, `postgres`).
2. Run HistoricalCollector for the 5 default symbols.
3. Verify record counts by symbol and by month.

**Validation checkpoints:**

- No missing month in the 12-month target period.
- No duplicates when backfill is re-run.
- Continuous 1-minute timestamp sequence.

### Step 4 — Add Data Gap Detection

- Create a job that scans for missing 1m candles per day/symbol.
- Store missing ranges in `data_gaps` table.
- Run automatic refill daily at 02:00 UTC.

**Expected outcome:** near-100% data coverage for the target time range.

### Step 5 — Merge Historical + Real-Time Pipeline

- Keep real-time writing to `price_ticks` after backfill completes.
- Use one schema and UTC timestamps across both sources.
- Prevent overlap duplication using the unique key.

**Expected outcome:** one continuous dataset from past to present in a single table.

### Step 6 — Add 1s Dataset for Scalping Tuning (Later Phase)

- Collect `1s` data only for recent 1 week and selected key symbols (e.g., `BTCUSDT`).
- Store in separate table (`price_ticks_1s`) to keep the 1m pipeline lean.
- Use for latency and intra-minute SL/TP behavior validation.

## Data Design

## Primary Table `price_ticks`

Minimum fields:

- `time` (TIMESTAMPTZ, UTC)
- `symbol` (TEXT)
- `interval` (TEXT: `1m`)
- `open`, `high`, `low`, `close` (NUMERIC)
- `volume` (NUMERIC)

Constraints/indexes:

- Unique: `(time, symbol, interval)`
- Index: `(symbol, time DESC)`

## Secondary Table `data_gaps`

- `symbol`
- `interval`
- `gap_start`
- `gap_end`
- `detected_at`
- `filled_at` (nullable)

## Proposed Configuration

```json
{
  "HistoricalData": {
    "Enabled": true,
    "StartDate": "2025-03-01",
    "EndDate": "2026-03-06",
    "Interval": "1m",
    "Symbols": ["BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT"],
    "BatchSize": 10000,
    "ParallelDownloads": 3,
    "BaseUrl": "https://data.binance.vision"
  },
  "GapFilling": {
    "Enabled": true,
    "DailyCheckUtc": "02:00"
  }
}
```

## Execution Checklist

- [x] Add real-time DB writer for DataIngestor.
- [x] Create `HistoricalCollector.Worker` project.
- [x] Implement Binance Vision downloader + CSV parser.
- [x] Implement bulk insert + idempotent upsert.
- [x] Backfill 12 months for 5 symbols (available monthly snapshots).
- [x] Add coverage verification script by day/symbol.
- [x] Enable daily gap detection + gap filling job.
- [x] Document local and Docker Compose run steps.

## Run Commands (Local + Docker)

### 1) Start infrastructure

```powershell
docker compose -f infrastructure/docker-compose.yml up -d redis
docker compose -f infrastructure/docker-compose.yml ps
```

Local PostgreSQL should be running on `localhost:5433` with database `cryptotrading`.

### 2) Run one-time 12-month backfill

```powershell
dotnet run --project .\src\Services\HistoricalCollector\HistoricalCollector.Worker\HistoricalCollector.Worker.csproj -- \
  --HistoricalData:Enabled=true \
  --HistoricalData:StartDate=2025-03-01 \
  --HistoricalData:EndDate=2026-03-06 \
  --GapFilling:Enabled=false
```

### 3) Run collector continuously for daily gap filling

```powershell
dotnet run --project .\src\Services\HistoricalCollector\HistoricalCollector.Worker\HistoricalCollector.Worker.csproj -- \
  --HistoricalData:Enabled=false \
  --GapFilling:Enabled=true
```

### 4) Verify coverage (day/symbol)

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-coverage.ps1 \
  -DbHost localhost -DbPort 5433 -DbName cryptotrading -DbUser postgres -DbPassword postgres
```

If `psql` is not in `PATH`, pass `-PsqlPath "C:\Program Files\PostgreSQL\17\bin\psql.exe"`.

## Definition of Done

1. Complete `1m` data for the latest 12 months exists for all target symbols.
2. Real-time stream continues writing into the same dataset.
3. No duplicates when backfill is re-run.
4. Day/symbol coverage is >= 99.9% (excluding unavoidable exchange/source outages).
5. Automated daily job exists to detect and fill gaps.

## Short Delivery Timeline

- **Day 1:** Real-time DB writer + schema constraints.
- **Day 2:** HistoricalCollector + parser + bulk insert.
- **Day 3:** Run 12-month backfill + coverage verification.
- **Day 4:** Gap detection/filling + hardening + operations runbook.

---

Note: After the algorithm is stable on `1m` data, extend to short-window `1s` data for scalping optimization and realistic slippage estimation.
