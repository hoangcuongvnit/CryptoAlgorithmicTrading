# Crypto Algorithmic Trading – Full Build Plan

## Project Decisions

| Decision | Choice | Rationale |
|---|---|---|
| **Scope** | Full system (all 6 phases) | End-to-end build |
| **Dashboard UI** | Blazor Server | C#/.NET ecosystem, SignalR live feeds |
| **Trading pairs** | 100 pairs, selectable from dashboard | Dynamic Redis channel subscriptions |
| **Trade mode** | Paper trading first (feature flag) | Safe default; toggle in `appsettings.json` |
| **AOT** | Enabled from day one | `[JsonSerializable]` source generators, no EF Core |
| **Indicators** | `Skender.Stock.Indicators` (not TA-Lib) | Pure .NET, AOT-safe; TA-Lib requires P/Invoke |
| **ORM** | Dapper + raw SQL (not EF Core) | EF Core reflection is incompatible with AOT |
| **Symbol config changes** | Via Redis `config:symbols` channel | Services update subscriptions without restarting |

---

## Step 1 — Monorepo Scaffold

Create the repository skeleton and shared MSBuild config.

**Files to create:**
- `global.json` — pins .NET 8 SDK version
- `.editorconfig` — formatting rules
- `Directory.Build.props` — shared MSBuild properties across all projects:
  - `<Nullable>enable</Nullable>`
  - `<ImplicitUsings>enable</ImplicitUsings>`
  - `<PublishAot>true</PublishAot>` (overrideable per project)
- `Directory.Packages.props` — centralized NuGet version management (CPM)
- `CryptoAlgorithmicTrading.sln` — solution file referencing all projects

**Directory structure to scaffold:**
```
src/Services/DataIngestor/Ingestor.Worker/
src/Services/Analyzer/Analyzer.Worker/
src/Services/Strategy/Strategy.Worker/
src/Services/Executor/Executor.API/
src/Services/RiskGuard/RiskGuard.API/
src/Services/Notifier/Notifier.Worker/
src/Shared/
src/Gateway/Gateway.API/
infrastructure/
tests/DataIngestor.Tests/
tests/Analyzer.Tests/
tests/Strategy.Tests/
tests/RiskGuard.Tests/
docs/
```

**Verification:** `dotnet build CryptoAlgorithmicTrading.sln` — zero errors on empty scaffold.

---

## Step 2 — Shared Class Library

**Project:** `src/Shared/Shared.csproj` — class library, referenced by all services.

**DTOs (`src/Shared/DTOs/`):**
- `PriceTick.cs` — `{ string Symbol, decimal Price, decimal Volume, decimal Open, decimal High, decimal Low, decimal Close, DateTime Timestamp, string Interval }`
- `TradeSignal.cs` — `{ string Symbol, decimal Rsi, decimal Ema9, decimal Ema21, decimal BbUpper, decimal BbMiddle, decimal BbLower, SignalStrength Strength, DateTime Timestamp }`
- `OrderRequest.cs` — `{ string Symbol, OrderSide Side, OrderType Type, decimal Quantity, decimal Price, decimal StopLoss, decimal TakeProfit, string StrategyName }`
- `OrderResult.cs` — `{ string OrderId, string Symbol, bool Success, decimal FilledPrice, decimal FilledQty, string ErrorMessage, DateTime Timestamp, bool IsPaperTrade }`
- `RiskValidationResult.cs` — `{ bool IsApproved, string RejectionReason, decimal AdjustedQuantity }`
- `SystemEvent.cs` — `{ SystemEventType Type, string ServiceName, string Message, DateTime Timestamp }`

**JSON source generators (`src/Shared/Json/TradingJsonContext.cs`):**
```csharp
[JsonSerializable(typeof(PriceTick))]
[JsonSerializable(typeof(TradeSignal))]
[JsonSerializable(typeof(OrderRequest))]
[JsonSerializable(typeof(OrderResult))]
[JsonSerializable(typeof(SystemEvent))]
[JsonSerializable(typeof(List<string>))]
internal partial class TradingJsonContext : JsonSerializerContext { }
```

**Constants (`src/Shared/Constants/RedisChannels.cs`):**
```csharp
public static class RedisChannels
{
    public static string Price(string symbol)   => $"price:{symbol}";
    public static string Signal(string symbol)  => $"signal:{symbol}";
    public const string TradesAudit             = "trades:audit";
    public const string SystemEvents            = "system:events";
    public const string ConfigSymbols           = "config:symbols";
}
```

**Extensions:**
- `SpanExtensions.cs` — helpers for `Span<decimal>` indicator window slicing
- `ChannelExtensions.cs` — `ReadAllAsync` wrappers for `System.Threading.Channels`

**Proto files (`src/Shared/ProtoFiles/`):**

`order_executor.proto`:
```protobuf
syntax = "proto3";
option csharp_namespace = "CryptoTrading.Executor.Grpc";

service OrderExecutorService {
  rpc PlaceOrder (PlaceOrderRequest) returns (PlaceOrderReply);
}

message PlaceOrderRequest {
  string symbol      = 1;
  string side        = 2;
  string order_type  = 3;
  double quantity    = 4;
  double price       = 5;
  double stop_loss   = 6;
  double take_profit = 7;
  string strategy    = 8;
}

message PlaceOrderReply {
  bool   success       = 1;
  string order_id      = 2;
  double filled_price  = 3;
  double filled_qty    = 4;
  string error_message = 5;
  bool   is_paper      = 6;
}
```

`risk_guard.proto`:
```protobuf
syntax = "proto3";
option csharp_namespace = "CryptoTrading.RiskGuard.Grpc";

service RiskGuardService {
  rpc ValidateOrder (ValidateOrderRequest) returns (ValidateOrderReply);
}

message ValidateOrderRequest {
  string symbol      = 1;
  string side        = 2;
  double quantity    = 3;
  double entry_price = 4;
  double stop_loss   = 5;
  double take_profit = 6;
}

message ValidateOrderReply {
  bool   is_approved        = 1;
  string rejection_reason   = 2;
  double adjusted_quantity  = 3;
}
```

---

## Step 3 — Infrastructure

**`infrastructure/docker-compose.yml`:**
```yaml
version: '3.9'

services:
  redis:
    image: redis:7-alpine
    container_name: trading_redis
    ports:
      - "6379:6379"
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 3

  postgres:
    image: timescale/timescaledb:latest-pg16
    container_name: trading_postgres
    env_file: .env
    environment:
      POSTGRES_USER: ${POSTGRES_USER:-trader}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-strongpassword}
      POSTGRES_DB: ${POSTGRES_DB:-cryptotrading}
    ports:
      - "5433:5433"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./init.sql:/docker-entrypoint-initdb.d/init.sql
    restart: unless-stopped
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U trader -d cryptotrading"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  postgres_data:

networks:
  default:
    name: trading_network
```

**`infrastructure/init.sql`** — TimescaleDB hypertables:
```sql
CREATE TABLE IF NOT EXISTS price_ticks (
    time        TIMESTAMPTZ NOT NULL,
    symbol      TEXT        NOT NULL,
    price       NUMERIC     NOT NULL,
    volume      NUMERIC     NOT NULL,
    open        NUMERIC,
    high        NUMERIC,
    low         NUMERIC,
    close       NUMERIC,
    interval    TEXT
);
SELECT create_hypertable('price_ticks', 'time', if_not_exists => TRUE);

CREATE TABLE IF NOT EXISTS trade_signals (
    time        TIMESTAMPTZ NOT NULL,
    symbol      TEXT        NOT NULL,
    rsi         NUMERIC,
    ema9        NUMERIC,
    ema21       NUMERIC,
    bb_upper    NUMERIC,
    bb_middle   NUMERIC,
    bb_lower    NUMERIC,
    strength    TEXT
);
SELECT create_hypertable('trade_signals', 'time', if_not_exists => TRUE);

CREATE TABLE IF NOT EXISTS orders (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    time        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    symbol      TEXT        NOT NULL,
    side        TEXT        NOT NULL,
    order_type  TEXT        NOT NULL,
    quantity    NUMERIC     NOT NULL,
    price       NUMERIC,
    filled_price NUMERIC,
    stop_loss   NUMERIC,
    take_profit NUMERIC,
    strategy    TEXT,
    is_paper    BOOLEAN     NOT NULL DEFAULT TRUE,
    success     BOOLEAN,
    error_msg   TEXT
);

CREATE TABLE IF NOT EXISTS active_symbols (
    symbol      TEXT PRIMARY KEY,
    enabled     BOOLEAN NOT NULL DEFAULT TRUE,
    added_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

**`infrastructure/.env.template`:**
```env
BINANCE_API_KEY=
BINANCE_API_SECRET=
BINANCE_USE_TESTNET=true
TELEGRAM_BOT_TOKEN=
TELEGRAM_CHAT_ID=
POSTGRES_USER=trader
POSTGRES_PASSWORD=strongpassword
POSTGRES_DB=cryptotrading
REDIS_CONNECTION=localhost:6379
```

---

## Step 4 — DataIngestor Service

**Project:** `src/Services/DataIngestor/Ingestor.Worker/Ingestor.Worker.csproj`  
**Template:** Worker Service  
**NuGet:** `Binance.Net`, `StackExchange.Redis`, `Polly`, `Polly.Extensions.Http`

**`Program.cs`:**
- Register `IConnectionMultiplexer` (Redis singleton)
- Register `IBinanceRestClient`, `IBinanceSocketClient`
- Register `RedisPublisher` as singleton
- Register `BinanceIngestorWorker` as hosted service
- Register `SymbolConfigListener` as hosted service (listens to `config:symbols`)
- Bind `TradingSettings` from `appsettings.json` (symbol list)

**`Workers/BinanceIngestorWorker.cs`:**
- On startup: reads active symbols from `appsettings.json` + PostgreSQL `active_symbols` table
- For each symbol: subscribes to `IBinanceSocketClient.SpotStreams.SubscribeToMiniTickerUpdatesAsync` (24h ticker) and `SubscribeToKlineUpdatesAsync` (interval: 1m)
- On each tick: maps to `PriceTick`, calls `RedisPublisher.PublishAsync`
- On disconnect: publishes `SystemEvent { Type=ConnectionLost }` to `system:events`; Polly exponential backoff attempts reconnect (max 5, 1s base delay, exponential)
- Implements `IAsyncDisposable` to unsubscribe all sockets on shutdown

**`Workers/SymbolConfigListener.cs`:**
- Subscribes to Redis `config:symbols` channel
- On message: parses new symbol list, diffs against current subscriptions, adds/removes sockets dynamically

**`Infrastructure/RedisPublisher.cs`:**
```csharp
public sealed class RedisPublisher(IConnectionMultiplexer redis)
{
    private readonly ISubscriber _sub = redis.GetSubscriber();

    public async ValueTask PublishAsync(PriceTick tick, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(tick, TradingJsonContext.Default.PriceTick);
        await _sub.PublishAsync(
            RedisChannel.Literal(RedisChannels.Price(tick.Symbol)), json);
    }
}
```

**`appsettings.json`:**
```json
{
  "Trading": {
    "Symbols": ["BTCUSDT","ETHUSDT","BNBUSDT","SOLUSDT","XRPUSDT"],
    "KlineInterval": "1m"
  },
  "Redis": { "Connection": "localhost:6379" },
  "ConnectionStrings": { "Postgres": "Host=localhost;Database=cryptotrading;Username=trader;Password=strongpassword" }
}
```

---

## Step 5 — Notifier Service

**Project:** `src/Services/Notifier/Notifier.Worker/Notifier.Worker.csproj`  
**Template:** Worker Service  
**NuGet:** `Telegram.Bot`, `StackExchange.Redis`

**`Workers/NotifierWorker.cs`:**
- On `ExecuteAsync`: immediately sends startup ping via Telegram: `🚀 CryptoTrader is ONLINE`
- Subscribes to Redis `system:events` channel (pattern: `SystemEvent` JSON)
- Subscribes to Redis `trades:audit` channel (pattern: `OrderResult` JSON)
- Delegates formatting to `TelegramNotifier`

**`Channels/TelegramNotifier.cs`:**

Message templates:
| Event | Template |
|---|---|
| System start | `🚀 CryptoTrader ONLINE \| {DateTime:HH:mm} UTC` |
| Connection lost | `⚠️ WS DISCONNECTED: {Symbol} \| Reconnecting...` |
| Connection restored | `✅ WS RESTORED: {Symbol}` |
| Order filled (paper) | `📄 PAPER {Side} {Symbol} @ {Price} \| SL:{SL} TP:{TP} \| Strategy:{Name}` |
| Order filled (live) | `✅ LIVE {Side} {Symbol} @ {Price} \| SL:{SL} TP:{TP}` |
| Order rejected | `❌ REJECTED {Symbol}: {Reason}` |
| Max drawdown | `🚨 MAX DRAWDOWN BREACHED – All trading halted` |

---

## Step 6 — SignalAnalyzer Service

**Project:** `src/Services/Analyzer/Analyzer.Worker/Analyzer.Worker.csproj`  
**Template:** Worker Service  
**NuGet:** `Skender.Stock.Indicators`, `StackExchange.Redis`

**`Workers/SignalAnalyzerWorker.cs`:**
- Subscribes to Redis `price:*` (wildcard `PSUBSCRIBE`) to receive ticks for all active symbols
- Maintains `ConcurrentDictionary<string, CircularBuffer<PriceTick>>` — rolling 200-candle buffer per symbol
- On each tick: appends to buffer; if buffer ≥ 50 candles, runs all calculators
- Publishes `TradeSignal` to `signal:{symbol}`
- Hot path: no LINQ, no heap allocations — uses `ArrayPool<decimal>` for indicator input arrays

**`Indicators/RsiCalculator.cs`:**
- Input: `ReadOnlySpan<decimal>` close prices
- Uses `Skender.Stock.Indicators` `GetRsi(period: 14)` on the last 50 candles
- Returns latest RSI value

**`Indicators/EmaCalculator.cs`:**
- Returns `(decimal Ema9, decimal Ema21)` pair from last 30 candles
- Uses `GetEma(lookbackPeriods: 9)` and `GetEma(lookbackPeriods: 21)`

**`Indicators/BollingerBandCalculator.cs`:**
- Returns `(decimal Upper, decimal Middle, decimal Lower)` from last 30 candles
- Uses `GetBollingerBands(lookbackPeriods: 20, standardDeviations: 2)`

**`SignalStrength` enum:**  
`None = 0`, `Weak = 1`, `Moderate = 2`, `Strong = 3`

Signal strength is determined by how many conditions are simultaneously met (RSI extreme + EMA crossover + BB band touch).

---

## Step 7 — Strategy Brain

**Project:** `src/Services/Strategy/Strategy.Worker/Strategy.Worker.csproj`  
**Template:** Worker Service  
**NuGet:** `StackExchange.Redis`, `Grpc.Net.Client`, `Google.Protobuf`, `Grpc.Tools`

**`Strategies/IStrategy.cs`:**
```csharp
public interface IStrategy
{
    string Name { get; }
    bool ShouldTrade(TradeSignal signal);
    OrderRequest BuildOrder(TradeSignal signal, decimal currentPrice);
}
```

**`Strategies/RsiReversalStrategy.cs`:**
- BUY when: RSI < 30 AND (EMA9 > EMA21) AND price touching BB lower band
- SELL when: RSI > 70 AND (EMA9 < EMA21) AND price touching BB upper band
- Stop-loss: 1.5% against entry; take-profit: 3% in direction (1:2 RR)

**`Strategies/EmaCrossoverStrategy.cs`:**
- BUY when: EMA9 crosses above EMA21 AND RSI between 40–60 (momentum confirmation)
- SELL when: EMA9 crosses below EMA21 AND RSI between 40–60
- Stop-loss: below/above the crossover candle low/high; take-profit: 2× stop distance

**`Workers/StrategyBrainWorker.cs`:**
- Subscribes to Redis `signal:*` (wildcard)
- Runs all registered `IStrategy` implementations against each `TradeSignal`
- On match: calls `RiskGuard` gRPC `ValidateOrder`; if approved, calls `OrderExecutor` gRPC `PlaceOrder`
- Logs all decisions (approved, rejected, no signal)

---

## Step 8 — RiskGuard Service

**Project:** `src/Services/RiskGuard/RiskGuard.API/RiskGuard.API.csproj`  
**Template:** ASP.NET Core gRPC service  
**NuGet:** `Grpc.AspNetCore`, `Dapper`, `Npgsql`

**`GrpcServices/RiskGuardService.cs`:**
- Implements `RiskGuardService.RiskGuardServiceBase` (generated from `risk_guard.proto`)
- Runs all `IRiskRule` implementations in order; first failure = reject

**`Rules/IRiskRule.cs`:**
```csharp
public interface IRiskRule
{
    string Name { get; }
    ValueTask<RuleResult> EvaluateAsync(ValidateOrderRequest request, CancellationToken ct);
}
```

**Rules:**

| Class | Logic |
|---|---|
| `MaxDrawdownRule` | Queries PostgreSQL daily PnL; rejects if loss > 5% of starting balance |
| `RiskRewardRule` | Calculates `(TakeProfit - Entry) / (Entry - StopLoss)`; rejects if < 2.0 |
| `PositionSizeRule` | Caps `Quantity × Price` at 2% of total account balance |
| `CooldownRule` | Checks last order time per symbol from DB; rejects if < 30 seconds ago |

**`appsettings.json`:**
```json
{
  "RiskRules": {
    "MaxDrawdownPercent": 5.0,
    "MinRiskReward": 2.0,
    "MaxPositionSizePercent": 2.0,
    "CooldownSeconds": 30
  }
}
```

---

## Step 9 — Order Executor Service

**Project:** `src/Services/Executor/Executor.API/Executor.API.csproj`  
**Template:** ASP.NET Core gRPC service  
**NuGet:** `Grpc.AspNetCore`, `Binance.Net`, `StackExchange.Redis`, `Polly`, `Dapper`, `Npgsql`

**`GrpcServices/OrderExecutorService.cs`:**
- Implements `OrderExecutorService.OrderExecutorServiceBase`
- Reads `PaperTradingMode` from config
  - **Paper mode**: simulates fill at bid price; writes `OrderResult` to DB and Redis Streams; no Binance call
  - **Live mode**: calls `BinanceOrderClient.PlaceOrderAsync` wrapped in Polly pipeline

**`Infrastructure/BinanceOrderClient.cs`:**
```csharp
// Polly pipeline: Retry(5, exponential) + CircuitBreaker(threshold=0.5, 60s break)
public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct)
{
    return await _resiliencePipeline.ExecuteAsync(async token =>
    {
        var result = await _binanceClient.SpotApi.Trading.PlaceOrderAsync(...);
        // map to OrderResult
    }, ct);
}
```

**Redis Streams audit log:**
```csharp
await _db.StreamAddAsync("trades:audit", new NameValueEntry[]
{
    new("symbol",  result.Symbol),
    new("side",    order.Side.ToString()),
    new("price",   result.FilledPrice.ToString()),
    new("qty",     result.FilledQty.ToString()),
    new("paper",   result.IsPaperTrade.ToString()),
    new("time",    result.Timestamp.ToString("O"))
});
```

**`appsettings.json`:**
```json
{
  "Trading": {
    "PaperTradingMode": true
  },
  "Binance": {
    "ApiKey": "",
    "ApiSecret": "",
    "UseTestnet": true
  }
}
```

---

## Step 10 — Blazor Server Dashboard + YARP Gateway

**Project:** `src/Gateway/Gateway.API/Gateway.API.csproj`  
**Template:** ASP.NET Core (Blazor Server + YARP)  
**NuGet:** `Yarp.ReverseProxy`, `StackExchange.Redis`, `Microsoft.AspNetCore.SignalR`, `Dapper`, `Npgsql`, `MudBlazor`

### Pages & Components

**`/` — Live Dashboard (`Pages/Dashboard.razor`)**
- Blazor table: one row per active symbol showing last price, RSI, EMA9 vs EMA21, last signal
- Updated in real-time via SignalR `PriceFeedHub`
- Daily PnL card (total across all symbols, paper vs live)
- Open positions table

**`/symbols` — Symbol Manager (`Pages/SymbolManager.razor`)**
- Fetches master list of 100 Binance USDT pairs via Binance REST API on page load
- MudBlazor `MudDataGrid` with checkboxes
- "Apply" button: saves selection to `active_symbols` PostgreSQL table + publishes `config:symbols` to Redis
- DataIngestor and Analyzer receive message and dynamically update subscriptions

**`/trades` — Trade History (`Pages/TradeHistory.razor`)**
- Paginated table from `orders` PostgreSQL table
- Filter by symbol, side, date range, paper/live

**`Hubs/PriceFeedHub.cs` (SignalR):**
```csharp
public class PriceFeedHub(IConnectionMultiplexer redis) : Hub
{
    public async Task SubscribeToSymbol(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, symbol);
    }
}
```

**`Services/RedisPriceFeedBridge.cs` (BackgroundService):**
- `PSUBSCRIBE price:*` — on every Redis message, forwards to relevant SignalR group via `IHubContext<PriceFeedHub>`

**YARP config (`appsettings.json`):**
```json
{
  "ReverseProxy": {
    "Routes": {
      "ingestor-health": {
        "ClusterId": "ingestor",
        "Match": { "Path": "/api/ingestor/{**catch-all}" }
      }
    },
    "Clusters": {
      "ingestor": {
        "Destinations": {
          "d1": { "Address": "http://localhost:5010" }
        }
      }
    }
  }
}
```

---

## Step 11 — Test Projects

**`tests/DataIngestor.Tests/`**
- `RedisPublisherTests.cs` — verifies JSON serialization output matches expected format (AOT-safe)
- `BinanceIngestorWorkerTests.cs` — mocks `IBinanceSocketClient`, verifies reconnect logic and `SystemEvent` published on disconnect

**`tests/Analyzer.Tests/`**
- `RsiCalculatorTests.cs` — known input array → expected RSI value ± 0.01
- `EmaCalculatorTests.cs` — known 20-candle sequence → correct EMA9/21 values
- `BollingerBandCalculatorTests.cs` — known sequence → correct upper/lower band values

**`tests/Strategy.Tests/`**
- `RsiReversalStrategyTests.cs` — RSI=28 → `ShouldTrade=true`; RSI=55 → `ShouldTrade=false`
- `EmaCrossoverStrategyTests.cs` — EMA9 crosses above EMA21 → BUY; below → SELL

**`tests/RiskGuard.Tests/`**
- `MaxDrawdownRuleTests.cs` — 6% daily loss → reject; 3% → approve
- `RiskRewardRuleTests.cs` — RR=1.5 → reject; RR=2.5 → approve
- `PositionSizeRuleTests.cs` — 3% of balance → reject; 1% → approve
- `CooldownRuleTests.cs` — last order 10s ago → reject; 60s ago → approve

---

## Step 12 — Dockerize All Services

Each service gets a `Dockerfile` using multi-stage AOT build:

```dockerfile
# Stage 1: Build & AOT publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Services/DataIngestor/Ingestor.Worker/Ingestor.Worker.csproj \
    -c Release -r linux-x64 --self-contained true \
    -p:PublishAot=true -o /app/publish

# Stage 2: Minimal runtime image
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["./Ingestor.Worker"]
```

**`docker-compose.yml` additions** — add all 6 service + gateway containers:
```yaml
  ingestor:
    build:
      context: ..
      dockerfile: src/Services/DataIngestor/Ingestor.Worker/Dockerfile
    depends_on:
      redis:    { condition: service_healthy }
      postgres: { condition: service_healthy }
    environment:
      - Redis__Connection=trading_redis:6379
      - ConnectionStrings__Postgres=Host=trading_postgres;...
    networks: [trading_network]
    restart: unless-stopped
```
_(Repeated for analyzer, strategy, executor, riskguard, notifier, gateway)_

---

## Verification Checklist

- [ ] `docker compose up -d` in `infrastructure/` → Redis and Postgres healthy
- [ ] `dotnet build CryptoAlgorithmicTrading.sln` → zero errors
- [ ] `dotnet test` → all unit tests green
- [ ] DataIngestor + Notifier running → Telegram receives `🚀 CryptoTrader ONLINE`
- [ ] `redis-cli SUBSCRIBE price:BTCUSDT` → price ticks streaming
- [ ] Enable 5 symbols in dashboard → all 5 channels appear in Redis
- [ ] Inject `TradeSignal` manually → paper order in audit log + Telegram notification
- [ ] Dashboard `/` → live price table updating
- [ ] `dotnet publish -r linux-x64 -p:PublishAot=true` → no trim warnings on any service
- [ ] `docker compose up` (full stack) → all containers healthy

---

## Service Port Map

| Service | Type | Port |
|---|---|---|
| DataIngestor | HTTP (health) | 5010 |
| Analyzer | HTTP (health) | 5011 |
| Strategy | HTTP (health) | 5012 |
| RiskGuard | gRPC | 5013 |
| OrderExecutor | gRPC | 5014 |
| Notifier | HTTP (health) | 5015 |
| Gateway (Blazor + YARP) | HTTP | 5000 |
| Redis | TCP | 6379 |
| PostgreSQL | TCP | 5433 |
