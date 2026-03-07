# Crypto Algorithmic Trading System

A high-performance, microservices-based algorithmic trading system for cryptocurrency markets, built on **.NET 8** with Native AOT optimization. Designed for low-latency execution, resilience, and 24/7 autonomous operation on Binance.

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Technical Stack](#technical-stack)
- [Project Structure](#project-structure)
- [Microservices](#microservices)
- [Communication Protocols](#communication-protocols)
- [Data Flow](#data-flow)
- [Getting Started](#getting-started)
- [Development Roadmap](#development-roadmap)
- [Engineering Principles](#engineering-principles)
- [Contributing](#contributing)

---

## Architecture Overview

Instead of a traditional monolith, this system is decomposed into **6 core microservices** following a lean microservices approach within a **Monorepo**. Each service has a single responsibility, can be scaled independently, and communicates via high-speed protocols (Redis Pub/Sub and gRPC) rather than slow HTTP REST calls.

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        CRYPTO TRADING SYSTEM                            │
│                                                                         │
│  [Binance WebSocket] ──► [Data Ingestor] ──► Redis Pub/Sub              │
│                                                      │                  │
│                                             [Signal Analyzer]           │
│                                                      │                  │
│                                           [Strategy Brain] ──gRPC──►   │
│                                                      │        [Risk Guard]
│                                                      │              │   │
│                                               [Order Executor] ◄────┘  │
│                                                      │                  │
│                                     [Notifier] ◄─────┴─────────────────┤
│                                         │                               │
│                                    [Telegram]                           │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Technical Stack

| Category        | Technology                                      | Purpose                                  |
|-----------------|-------------------------------------------------|------------------------------------------|
| **Runtime**     | .NET 8 (Native AOT)                             | Low memory consumption, fast startup     |
| **Architecture**| Microservices + Monorepo                        | Independent scaling, clean separation   |
| **Price Feed**  | Redis Pub/Sub                                   | Ultra-low latency market data relay      |
| **Internal RPC**| gRPC (Protobuf)                                 | Typed, binary-efficient service calls    |
| **Message Queue**| NATS / Redis Streams                           | Durable event log, no lost orders       |
| **Database**    | PostgreSQL + TimescaleDB                        | Time-series OHLCV & trade history        |
| **Resilience**  | Polly (Retry + Circuit Breaker)                 | Fault-tolerant order execution           |
| **Exchange**    | Binance.Net                                     | WebSocket feed + REST order placement    |
| **Indicators**  | TA-Lib (via .NET bindings)                      | RSI, EMA, Bollinger Bands, etc.          |
| **Notifications**| Telegram Bot API                               | Real-time alerts to mobile               |
| **Deployment**  | Docker Compose                                  | One-command local infrastructure         |
| **Proxy**       | YARP Reverse Proxy                              | API gateway for any web dashboard        |

---

## Project Structure

```
CryptoAlgorithmicTrading/                   # Monorepo root
│
├── src/
│   ├── Services/
│   │   ├── DataIngestor/                   # Binance WebSocket consumer
│   │   │   └── Ingestor.Worker/            # BackgroundService
│   │   │       ├── Workers/
│   │   │       │   └── BinanceIngestorWorker.cs
│   │   │       ├── Infrastructure/
│   │   │       │   └── RedisPublisher.cs
│   │   │       ├── appsettings.json
│   │   │       ├── Dockerfile
│   │   │       └── Program.cs
│   │   │
│   │   ├── Analyzer/                       # Technical indicator engine
│   │   │   └── Analyzer.Worker/            # BackgroundService
│   │   │       ├── Workers/
│   │   │       │   └── SignalAnalyzerWorker.cs
│   │   │       ├── Indicators/
│   │   │       │   ├── RsiCalculator.cs
│   │   │       │   ├── EmaCalculator.cs
│   │   │       │   └── BollingerBandCalculator.cs
│   │   │       ├── appsettings.json
│   │   │       ├── Dockerfile
│   │   │       └── Program.cs
│   │   │
│   │   ├── Strategy/                       # Decision-making brain
│   │   │   └── Strategy.Worker/            # BackgroundService
│   │   │       ├── Workers/
│   │   │       │   └── StrategyBrainWorker.cs
│   │   │       ├── Strategies/             # Individual algorithm classes
│   │   │       │   ├── IStrategy.cs
│   │   │       │   ├── RsiReversalStrategy.cs
│   │   │       │   └── EmaCrossoverStrategy.cs
│   │   │       ├── appsettings.json
│   │   │       ├── Dockerfile
│   │   │       └── Program.cs
│   │   │
│   │   ├── Executor/                       # Order execution via Binance API
│   │   │   └── Executor.API/               # gRPC Server
│   │   │       ├── GrpcServices/
│   │   │       │   └── OrderExecutorService.cs
│   │   │       ├── Infrastructure/
│   │   │       │   └── BinanceOrderClient.cs
│   │   │       ├── appsettings.json
│   │   │       ├── Dockerfile
│   │   │       └── Program.cs
│   │   │
│   │   ├── RiskGuard/                      # Last safety checkpoint
│   │   │   └── RiskGuard.API/              # gRPC Server
│   │   │       ├── GrpcServices/
│   │   │       │   └── RiskGuardService.cs
│   │   │       ├── Rules/
│   │   │       │   ├── MaxDrawdownRule.cs
│   │   │       │   └── RiskRewardRule.cs
│   │   │       ├── appsettings.json
│   │   │       ├── Dockerfile
│   │   │       └── Program.cs
│   │   │
│   │   └── Notifier/                       # Telegram & alert dispatcher
│   │       └── Notifier.Worker/            # BackgroundService
│   │           ├── Workers/
│   │           │   └── NotifierWorker.cs
│   │           ├── Channels/
│   │           │   └── TelegramNotifier.cs
│   │           ├── appsettings.json
│   │           ├── Dockerfile
│   │           └── Program.cs
│   │
│   ├── Shared/                             # Cross-cutting concerns
│   │   ├── DTOs/
│   │   │   ├── PriceTick.cs
│   │   │   ├── TradeSignal.cs
│   │   │   └── OrderRequest.cs
│   │   ├── ProtoFiles/
│   │   │   ├── order_executor.proto
│   │   │   └── risk_guard.proto
│   │   ├── Constants/
│   │   │   └── RedisChannels.cs
│   │   └── CommonExtensions/
│   │       └── SpanExtensions.cs
│   │
│   └── Gateway/                            # Optional: Web dashboard proxy
│       └── Gateway.API/
│           └── Program.cs                  # YARP Reverse Proxy
│
├── infrastructure/
│   ├── docker-compose.yml                  # Redis + PostgreSQL/TimescaleDB
│   └── docker-compose.override.yml        # Local dev overrides
│
├── tests/
│   ├── DataIngestor.Tests/
│   ├── Analyzer.Tests/
│   ├── Strategy.Tests/
│   └── RiskGuard.Tests/
│
├── docs/
│   └── architecture.md
│
├── CryptoAlgorithmicTrading.sln
└── README.md
```

---

## Microservices

### 1. Data Ingestor
**Path:** `src/Services/DataIngestor/Ingestor.Worker`

Connects to the Binance WebSocket feed and streams real-time market data into the system. Runs as a `BackgroundService` with automatic reconnection logic.

- Subscribes to `BTCUSDT` Ticker and K-Line (1-minute candles) via `Binance.Net`
- Publishes `PriceTick` messages to Redis channel `price:BTCUSDT`
- Handles WebSocket disconnections with Polly exponential backoff
- Notifies the Notifier service on startup and connection failure

```
Binance WebSocket  →  BinanceIngestorWorker  →  RedisPublisher  →  [Redis: price:BTCUSDT]
```

---

### 2. Signal Analyzer
**Path:** `src/Services/Analyzer/Analyzer.Worker`

CPU-intensive service that subscribes to the price feed and computes technical indicators. Can be horizontally scaled to handle multiple trading pairs simultaneously.

- Subscribes to Redis channel `price:BTCUSDT`
- Computes: **RSI (14)**, **EMA (9/21)**, **Bollinger Bands (20, 2σ)**
- Publishes `TradeSignal` events to Redis channel `signal:BTCUSDT`
- Uses `Span<T>` and buffer pooling to avoid heap allocations in hot paths

---

### 3. Strategy Brain
**Path:** `src/Services/Strategy/Strategy.Worker`

The algorithmic decision engine. Subscribes to signals from the Analyzer, applies configurable strategy rules, and generates `OrderRequest` objects when high-probability setups are detected.

- Subscribes to Redis channel `signal:BTCUSDT`
- Implements `IStrategy` interface for pluggable algorithm classes
- Built-in strategies: `RsiReversalStrategy`, `EmaCrossoverStrategy`
- Calls **Risk Guard** via gRPC before forwarding an order to Executor

```
[Signal Analyzer] → Redis → StrategyBrainWorker → gRPC → [Risk Guard]
                                                        ↓ (approved)
                                                   [Order Executor]
```

---

### 4. Order Executor
**Path:** `src/Services/Executor/Executor.API`

Exposes a **gRPC server** that receives validated `OrderRequest` messages and executes them via the Binance REST API. Security-critical: API keys are managed via environment variables / secrets only.

- Accepts `PlaceOrder` RPC calls (defined in `order_executor.proto`)
- Uses `Binance.Net` for order placement (Market, Limit, Stop-Limit)
- Polly Circuit Breaker prevents cascading failures during API outages
- Publishes execution results to Redis Streams for durable audit log

---

### 5. Risk Guard
**Path:** `src/Services/RiskGuard/RiskGuard.API`

The last line of defense before any order reaches the exchange. Exposes a **gRPC server** that validates every order against configurable risk rules. An order is rejected if any rule fails.

| Rule                  | Description                                        |
|-----------------------|----------------------------------------------------|
| `MaxDrawdownRule`     | Rejects orders if daily PnL exceeds max drawdown % |
| `RiskRewardRule`      | Requires minimum Risk/Reward ratio (e.g., 1:2)     |
| `PositionSizeRule`    | Caps position size as % of total account balance   |
| `CooldownRule`        | Enforces minimum time between consecutive trades   |

---

### 6. Notifier
**Path:** `src/Services/Notifier/Notifier.Worker`

An event-driven notification dispatcher that sends real-time alerts to Telegram. Keeps the trader informed without requiring them to monitor a screen.

**Triggers:**
- System startup / service health check
- WebSocket disconnection or reconnection
- Order placed, filled, or rejected by Risk Guard
- Max drawdown threshold breached (emergency alert)

---

## Communication Protocols

Speed is money in trading. HTTP REST APIs are not used for inter-service communication.

```
┌─────────────────────────────────────────────────────────────────┐
│  TIER 1 – Redis Pub/Sub                                         │
│  Use case: Market data relay (Ingestor → Analyzer)              │
│  Latency:  < 1ms on localhost                                   │
│  Channels: price:{symbol}, signal:{symbol}                      │
├─────────────────────────────────────────────────────────────────┤
│  TIER 2 – gRPC (Protocol Buffers)                               │
│  Use case: Strategy → Risk Guard → Order Executor               │
│  Latency:  < 5ms, strongly-typed contracts via .proto files     │
│  Benefits: Binary serialization, streaming support, codegen     │
├─────────────────────────────────────────────────────────────────┤
│  TIER 3 – Redis Streams / NATS                                  │
│  Use case: Durable event log for all trade events               │
│  Guarantee: At-least-once delivery, survives service restarts   │
└─────────────────────────────────────────────────────────────────┘
```

---

## Data Flow

```
1. [Binance Exchange]
        │  WebSocket (ticker + kline_1m)
        ▼
2. [Data Ingestor]
        │  Redis PUBLISH "price:BTCUSDT"  →  PriceTick { Symbol, Price, Volume, Timestamp }
        ▼
3. [Signal Analyzer]
        │  RSI=42, EMA9 crosses above EMA21, BB lower band touched
        │  Redis PUBLISH "signal:BTCUSDT"  →  TradeSignal { Symbol, Indicators, Strength }
        ▼
4. [Strategy Brain]
        │  Pattern match: "RSI oversold + EMA crossover = BUY signal"
        │  gRPC →  RiskGuard.Validate(OrderRequest)
        ▼
5. [Risk Guard]
        │  Check: DrawdownOk=true, RR=2.3 (≥ 2.0), PositionSize=1.5% (≤ 2%)
        │  gRPC response: Approved
        ▼
6. [Order Executor]
        │  Binance.Net PlaceOrder(BTCUSDT, BUY, LIMIT, qty=0.001, price=X)
        │  Redis Streams PUBLISH "trades:audit"  →  OrderResult
        ▼
7. [Notifier]
        │  Telegram: "✅ BUY BTCUSDT @ 65,420 USDT | SL: 64,800 | TP: 66,660"
        ▼
   [Trader's Telegram]
```

---

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- Binance API Key & Secret (Testnet recommended for development)
- Telegram Bot Token + Chat ID

### Quick References
- **Phase 4 Runbook**: [PHASE4_RUNBOOK.md](docs/PHASE4_RUNBOOK.md) – Complete service startup and testing guide
- **Phase 5 Observability**: [PHASE5_OBSERVABILITY.md](docs/PHASE5_OBSERVABILITY.md) – Metrics, Prometheus, and Grafana setup

### 1. Clone the Repository

```bash
git clone https://github.com/your-username/CryptoAlgorithmicTrading.git
cd CryptoAlgorithmicTrading
```

### 2. Configure Environment Variables

Create a `.env` file in the `infrastructure/` directory:

```env
# Binance
BINANCE_API_KEY=your_api_key_here
BINANCE_API_SECRET=your_api_secret_here
BINANCE_USE_TESTNET=true

# Telegram
TELEGRAM_BOT_TOKEN=your_bot_token_here
TELEGRAM_CHAT_ID=your_chat_id_here

# PostgreSQL
POSTGRES_USER=trader
POSTGRES_PASSWORD=strongpassword
POSTGRES_DB=cryptotrading

# Redis
REDIS_CONNECTION=localhost:6379
```

### 3. Start Infrastructure

```bash
cd infrastructure

# Core infrastructure (Redis + PostgreSQL)
docker compose up -d

# Optional: Observability stack (Prometheus + Grafana + Jaeger)
docker compose -f docker-compose.yml -f docker-compose-observability.yml up -d
```

This starts:
- **Redis** on `localhost:6379`
- **PostgreSQL + TimescaleDB** on `localhost:5433`
- **Prometheus** on `localhost:9090` (optional)
- **Grafana** on `localhost:3000` (optional, default: admin/admin)
- **Jaeger** on `localhost:16686` (optional)

### 4. Run Services Locally

```bash
# Terminal 1 – Data Ingestor
cd src/Services/DataIngestor/Ingestor.Worker
dotnet run

# Terminal 2 – Signal Analyzer
cd src/Services/Analyzer/Analyzer.Worker
dotnet run

# Terminal 3 – Strategy Brain
cd src/Services/Strategy/Strategy.Worker
dotnet run

# Terminal 4 – Risk Guard (gRPC server)
cd src/Services/RiskGuard/RiskGuard.API
dotnet run

# Terminal 5 – Order Executor (gRPC server)
cd src/Services/Executor/Executor.API
dotnet run

# Terminal 6 – Notifier
cd src/Services/Notifier/Notifier.Worker
dotnet run
```

### Infrastructure: `docker-compose.yml`

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
    environment:
      POSTGRES_USER: ${POSTGRES_USER:-trader}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-strongpassword}
      POSTGRES_DB: ${POSTGRES_DB:-cryptotrading}
    ports:
      - "5433:5433"
    volumes:
      - postgres_data:/var/lib/postgresql/data
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

---

## Development Roadmap

### Phase 1 – Foundation ✅
- [x] Design monorepo project structure
- [x] Initialize .NET 8 solution with all service projects
- [x] Implement `Shared` library (DTOs, Proto files, Constants)
- [x] Build `DataIngestor` – Binance WebSocket + Redis publisher
- [x] Build `Notifier` – Telegram Bot integration
- [x] Docker Compose for Redis + PostgreSQL/TimescaleDB

### Phase 2 – Signal Pipeline ✅
- [x] Implement `SignalAnalyzer` with RSI, EMA, Bollinger Bands
- [x] Design and implement `TradeSignal` schema in TimescaleDB
- [x] Unit tests for all indicator calculations

### Phase 3 – Trading Brain ✅
- [x] Implement `IStrategy` interface and first two strategies
- [x] Build `RiskGuard` gRPC service with all risk rules
- [x] Integration test: Signal → Brain → RiskGuard flow

### Phase 4 – Order Execution ✅
- [x] Build `OrderExecutor` gRPC service with paper trading simulator
- [x] Polly Circuit Breaker for Binance API calls (5 retries, exponential backoff)
- [x] Paper trading mode (deterministic fills at reference price + slippage)
- [x] Redis Streams audit log for all order events with event versioning
- [x] PostgreSQL persistence with complete order metadata
- [x] End-to-end integration test (Signal → RiskGuard → Executor → Persistence)

**Status**: All services running and verified. Orders persisting, audit trail active. See [PHASE4_RUNBOOK.md](docs/PHASE4_RUNBOOK.md)

### Phase 5 – Observability & Hardening (In Progress)
- [x] OpenTelemetry integration with Executor.API
- [x] Custom metrics class for order execution tracking
- [x] Prometheus time-series database configuration
- [x] Grafana dashboards (order execution metrics, latency, fill rates)
- [x] Docker Compose with observability stack (Prometheus + Grafana + Jaeger)
- [ ] Extend tracing to RiskGuard.API and Strategy.Worker
- [ ] Structured logging with Serilog integration
- [ ] Load testing with simulated rapid price feeds (1000+ signals/sec)
- [ ] Native AOT compilation validation for all services
- [ ] Alert rules for order rejection and latency anomalies

**Documentation**: See [PHASE5_OBSERVABILITY.md](docs/PHASE5_OBSERVABILITY.md) for detailed setup and monitoring guide.

### Phase 6 – Web Dashboard & Reporting
- [ ] YARP Gateway API setup
- [ ] Dashboard API endpoints (orders, PnL, signals, risk metrics)
- [ ] Blazor or React frontend for real-time monitoring
- [ ] Order history & trade analysis API
- [ ] WebSocket endpoint for live signal/order feed

---

## Engineering Principles

### Clean Architecture (Lean)
Each service follows a trimmed-down Clean Architecture with three layers:

```
Domain/          – Core models and business rules (no external dependencies)
Application/     – Use cases, service interfaces, signal handlers
Infrastructure/  – Binance.Net, Redis, gRPC clients, DB repositories
```

### Performance-First Code

- All worker hot paths avoid LINQ and `async` state machine overhead where possible
- `Span<T>` and `ArrayPool<T>` used for indicator buffer calculations
- `Channel<T>` (System.Threading.Channels) for in-process producer/consumer queues
- `RecyclableMemoryStream` for network buffer management

### Resilience Patterns (Polly)

```csharp
// Example: Retry with exponential backoff for Binance API
var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 5,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(60)
    })
    .Build();
```

### BackgroundService Pattern

All long-running workers inherit from `BackgroundService` and register via `IHostedService`:

```csharp
public class BinanceIngestorWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Reconnect loop with Polly
        // Subscribe to WebSocket streams
        // Publish PriceTick to Redis
    }
}
```

---

## Contributing

This project is in active development. To contribute:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-strategy`)
3. Follow the existing code structure and engineering principles
4. Add unit tests for any new indicator or strategy logic
5. Submit a Pull Request with a clear description of the change

---

## Disclaimer

> **This software is for educational and research purposes only.**
> Algorithmic trading involves significant financial risk. Past performance of any strategy does not guarantee future results. Never trade with funds you cannot afford to lose. The authors are not responsible for any financial losses incurred through the use of this software.

---

## License

[MIT License](LICENSE) – See LICENSE file for details.
