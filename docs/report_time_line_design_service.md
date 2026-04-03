# Timeline Logging Service - Design & Implementation Plan

**Status**: Planning Phase  
**Created**: April 2, 2026  
**Purpose**: Centralized coin activity timeline tracking with MongoDB storage for efficient historical data retrieval and dashboard display

---

## 1. EXECUTIVE SUMMARY

The Timeline Logging Service is a dedicated microservice that captures and stores all chronological activities related to each cryptocurrency symbol throughout the trading system. This service provides a centralized, queryable history of:

- Price movements and indicator changes
- Trading signals and their confidence
- Order placements, executions, and closures
- Risk evaluations and rejections
- Liquidations and session events
- Funding rate changes
- Market regime shifts
- System-level decisions

The service uses **MongoDB** as its primary data store for:
- Flexible schema (events may evolve)
- Efficient time-series queries (TTL indexes, aggregation pipelines)
- Horizontal scalability
- Rich indexing capabilities
- Natural JSON alignment with event data

---

## 2. ARCHITECTURE OVERVIEW

### 2.1 System Data Flow

```
Ingestor          ┌─────────────┐       
                  │   Analyzer  │       
Strategy          │             │       RiskGuard
                  │ Executor    │       
                  └────────────┐│       
                               ││
                    ┌──────────┘│       
                    │    Redis  │       
                    │  Pub/Sub  │       
                    │  Channel: │       
                    │coin:*:log │       
                    └──────────┬┘       
                               │        
                ┌──────────────┴────────────┐
                │ TimelineLogger.Worker     │
                │ (Background Service)      │
                │                           │
                │ - Redis Subscriber        │
                │ - Event Parser            │
                │ - MongoDB Batch Writer    │
                └──────────────┬────────────┘
                               │
                    ┌──────────▼──────────┐
                    │   MongoDB           │
                    │  (Time-Series DB)   │
                    │                     │
                    │ Collections:        │
                    │  - coin_events      │
                    │  - event_summary    │
                    │  - daily_activity   │
                    └─────────┬───────────┘
                              │
                ┌─────────────┴─────────────┐
                │  TimelineLogger.API       │
                │  (REST Endpoints)         │
                │                           │
                │ /api/timeline/events      │
                │ /api/timeline/summary     │
                │ /api/timeline/export      │
                │ /api/timeline/dashboard   │
                └─────────────────────────┘
                              ▲
                              │
                        Frontend / Dashboard
```

### 2.2 Service Architecture

```
TimelineLogger Service (New)
├── TimelineLogger.Worker (BackgroundService)
│   ├── RedisSubscriber
│   │   └── Listens to: coin:*:log channel
│   ├── EventProcessor
│   │   ├── EventParser
│   │   ├── ValidationEngine
│   │   └── EnrichmentService
│   ├── MongodbWriter
│   │   ├── BatchQueue
│   │   └── BulkInsertExecutor
│   └── Workers
│       ├── CoinEventLoggerWorker.cs
│       ├── DailyAggregationWorker.cs
│       └── CacheWarmupWorker.cs
│
└── TimelineLogger.API (REST Service)
    ├── Controllers
    │   ├── TimelineEventsController.cs
    │   ├── TimelineSummaryController.cs
    │   ├── TimelineExportController.cs
    │   └── HealthController.cs
    ├── Services
    │   ├── TimelineQueryService.cs
    │   ├── TimelineAggregationService.cs
    │   ├── ExportService.cs
    │   └── CacheService.cs
    └── Configuration
        ├── MongoSettings.cs
        └── TimelineSettings.cs
```

---

## 3. MONGODB DATA MODEL

### 3.1 Collections Schema

#### 3.1.1 Collection: `coin_events`

Primary collection storing individual events with TTL index (90 days retention by default).

```json
{
  "_id": ObjectId,
  "symbol": "BTCUSDT",
  "event_type": "ORDER_PLACED",
  "event_category": "TRADING",
  "timestamp": ISODate("2026-04-02T10:30:45.123Z"),
  "unix_timestamp": 1743667845123,
  "event_data": {
    "order_id": "ORD-12345",
    "side": "BUY",
    "quantity": 0.5,
    "price": 45000.00,
    "stop_loss": 44500.00,
    "take_profit": 46000.00,
    // ... event-specific fields
  },
  "source_service": "Executor",
  "severity": "INFO",
  "correlation_id": "CORR-XYZ123",
  "user_id": null,
  "session_id": "SESSION-2026-04-02-0",
  "metadata": {
    "market_regime": "TrendingUp",
    "rsi_value": 62.5,
    "funding_rate": 0.0001
  },
  "tags": ["buy", "entry", "strong_signal"],
  "created_at": ISODate("2026-04-02T10:30:45.123Z"),
  "expires_at": ISODate("2026-06-01T10:30:45.123Z")  // TTL
}
```

**Indexes**:
```javascript
// Compound index for efficient filtering
db.coin_events.createIndex({ symbol: 1, timestamp: -1 })
db.coin_events.createIndex({ symbol: 1, event_type: 1, timestamp: -1 })
db.coin_events.createIndex({ event_category: 1, timestamp: -1 })
db.coin_events.createIndex({ source_service: 1, timestamp: -1 })
db.coin_events.createIndex({ session_id: 1, timestamp: -1 })

// TTL index (30 days by default, configurable)
db.coin_events.createIndex({ expires_at: 1 }, { expireAfterSeconds: 0 })

// Text search index for quick searches
db.coin_events.createIndex({ "event_data": "text", tags: "text" })
```

#### 3.1.2 Event Types Configuration

```javascript
// Define all event types across services
const EVENT_TYPES = {
  // Ingestor Events
  PRICE_TICK_RECEIVED: {
    category: "PRICE_DATA",
    severity: "DEBUG",
    retention_days: 7
  },
  
  // Analyzer Events
  SIGNAL_GENERATED: {
    category: "TRADING_SIGNAL",
    severity: "INFO",
    retention_days: 90
  },
  INDICATOR_CALCULATED: {
    category: "ANALYSIS",
    severity: "DEBUG",
    retention_days: 30
  },
  MARKET_REGIME_CHANGED: {
    category: "MARKET",
    severity: "INFO",
    retention_days: 90
  },
  
  // Strategy Events
  STRATEGY_EVALUATED: {
    category: "STRATEGY",
    severity: "INFO",
    retention_days: 90
  },
  ORDER_MAPPED: {
    category: "STRATEGY",
    severity: "INFO",
    retention_days: 90
  },
  
  // RiskGuard Events
  RISK_VALIDATION_STARTED: {
    category: "RISK",
    severity: "INFO",
    retention_days: 90
  },
  RISK_RULE_EVALUATED: {
    category: "RISK",
    severity: "INFO",
    retention_days: 90
  },
  RISK_VALIDATION_REJECTED: {
    category: "RISK",
    severity: "WARNING",
    retention_days: 90
  },
  RISK_VALIDATION_APPROVED: {
    category: "RISK",
    severity: "INFO",
    retention_days: 90
  },
  
  // Executor Events
  ORDER_PLACED: {
    category: "TRADING",
    severity: "INFO",
    retention_days: 365
  },
  ORDER_FILLED: {
    category: "TRADING",
    severity: "INFO",
    retention_days: 365
  },
  ORDER_PARTIALLY_FILLED: {
    category: "TRADING",
    severity: "INFO",
    retention_days: 365
  },
  ORDER_CANCELLED: {
    category: "TRADING",
    severity: "INFO",
    retention_days: 365
  },
  POSITION_OPENED: {
    category: "POSITION",
    severity: "INFO",
    retention_days: 365
  },
  POSITION_CLOSED: {
    category: "POSITION",
    severity: "INFO",
    retention_days: 365
  },
  TAKE_PROFIT_HIT: {
    category: "POSITION",
    severity: "INFO",
    retention_days: 365
  },
  STOP_LOSS_HIT: {
    category: "POSITION",
    severity: "INFO",
    retention_days: 365
  },
  POSITION_LIQUIDATED: {
    category: "LIQUIDATION",
    severity: "WARNING",
    retention_days: 365
  },
  SESSION_CLOSED: {
    category: "SESSION",
    severity: "INFO",
    retention_days: 365
  },
  
  // Notifier Events
  NOTIFICATION_SENT: {
    category: "NOTIFICATION",
    severity: "INFO",
    retention_days: 90
  },
  
  // HouseKeeper Events
  CLEANUP_EXECUTED: {
    category: "MAINTENANCE",
    severity: "INFO",
    retention_days: 180
  }
};
```

#### 3.1.3 Collection: `event_summary`

Aggregated daily/hourly summaries for fast dashboard queries.

```json
{
  "_id": {
    "symbol": "BTCUSDT",
    "date": "2026-04-02",
    "hour": 10  // optional for hourly
  },
  "period": "daily",  // or "hourly"
  "symbol": "BTCUSDT",
  "date": "2026-04-02",
  "hour": 10,
  "event_counts": {
    "ORDER_PLACED": 5,
    "ORDER_FILLED": 4,
    "SIGNAL_GENERATED": 12,
    "RISK_VALIDATION_REJECTED": 2,
    // ... all event types
  },
  "total_events": 45,
  "signals_strong": 8,
  "signals_neutral": 3,
  "signals_weak": 1,
  "orders_placed": 5,
  "orders_filled": 4,
  "win_rate": 0.75,
  "pnl": 250.50,
  "market_regime": "TrendingUp",
  "average_rsi": 62.5,
  "funding_rate": 0.0001,
  "last_price": 45234.50,
  "high_price": 45500.00,
  "low_price": 44800.00,
  "created_at": ISODate("2026-04-02T23:59:59.999Z"),
  "updated_at": ISODate("2026-04-03T00:30:45.123Z")
}
```

#### 3.1.4 Collection: `daily_activity`

Comprehensive daily reports for export and long-term archival.

```json
{
  "_id": {
    "symbol": "BTCUSDT",
    "date": "2026-04-02"
  },
  "symbol": "BTCUSDT",
  "date": "2026-04-02",
  "trading_day_summary": {
    "sessions": 6,
    "total_orders": 12,
    "filled_orders": 10,
    "cancelled_orders": 2,
    "total_positions": 8,
    "closed_positions": 7,
    "open_positions": 1,
    "winning_positions": 6,
    "losing_positions": 1
  },
  "financial_metrics": {
    "total_pnl": 450.75,
    "gross_pnl": 500.00,
    "loss_amount": -49.25,
    "win_rate": 0.875,
    "avg_win": 83.33,
    "avg_loss": -49.25,
    "largest_win": 150.00,
    "largest_loss": -49.25,
    "profit_factor": 10.15
  },
  "risk_metrics": {
    "max_drawdown": 2.5,
    "total_rejections": 5,
    "risk_score": 3.5
  },
  "signals_analysis": {
    "total_signals": 24,
    "strong_signals": 16,
    "neutral_signals": 6,
    "weak_signals": 2,
    "signal_accuracy": 0.92
  },
  "timeline_events": [
    {
      "time": "10:30:45",
      "event_type": "SIGNAL_GENERATED",
      "details": "Strong BUY signal"
    },
    // ... all events
  ],
  "created_at": ISODate("2026-04-02T23:59:59.999Z"),
  "archived": false
}
```

---

## 4. MESSAGE ROUTING & EVENT PUBLISHING

### 4.1 Redis Pub/Sub Channel Specification

**Channel Name**: `coin:{SYMBOL}:log`

Example: `coin:BTCUSDT:log`, `coin:ETHUSDT:log`

**Message Format (JSON)**:

```json
{
  "correlation_id": "CORR-XYZ-123456",
  "timestamp": "2026-04-02T10:30:45.123Z",
  "source_service": "Executor",
  "event_type": "ORDER_PLACED",
  "symbol": "BTCUSDT",
  "session_id": "SESSION-2026-04-02-0",
  "severity": "INFO",
  "payload": {
    "order_id": "ORD-12345",
    "side": "BUY",
    "quantity": 0.5,
    "price": 45000.00,
    // ... event-specific data
  },
  "metadata": {
    "market_regime": "TrendingUp",
    "current_rsi": 62.5,
    "funding_rate": 0.0001
  },
  "tags": ["buy", "entry", "strong_signal"]
}
```

### 4.2 Publishing Integration Points

**Each service publishes events from the following points**:

#### 4.2.1 Ingestor Service
```
PriceTickPersistenceWorker.cs
├── After successful price tick insert
└── Event: PRICE_TICK_RECEIVED
```

#### 4.2.2 Analyzer Service
```
SignalAnalyzerWorker.cs
├── After signal generation
├── Event: SIGNAL_GENERATED
├── Event: INDICATOR_CALCULATED
└── Event: MARKET_REGIME_CHANGED

FundingRateFetcherWorker.cs
├── After funding rate update
└── Event: FUNDING_RATE_UPDATED
```

#### 4.2.3 Strategy Service
```
Strategy.Worker.cs
├── After signal evaluation
├── Event: STRATEGY_EVALUATED
├── Event: ORDER_MAPPED (before sending to RiskGuard)
└── Sends OrderRequest details
```

#### 4.2.4 RiskGuard Service
```
RiskValidationEngine.cs
├── Event: RISK_VALIDATION_STARTED
├── For each rule evaluation
│   └── Event: RISK_RULE_EVALUATED (includes rule name, pass/fail, reason)
├── Final decision: RISK_VALIDATION_APPROVED or RISK_VALIDATION_REJECTED
└── Includes all rule rejection reasons
```

#### 4.2.5 Executor Service
```
OrderExecutionService.cs
├── Event: ORDER_PLACED
├── Event: ORDER_FILLED
├── Event: ORDER_PARTIALLY_FILLED
└── Event: ORDER_CANCELLED

PositionLifecycleManager.cs
├── Event: POSITION_OPENED
├── Event: POSITION_CLOSED
├── Event: TAKE_PROFIT_HIT
└── Event: STOP_LOSS_HIT

LiquidationOrchestrator.cs
└── Event: POSITION_LIQUIDATED

SessionExitOnlyMonitorService.cs
└── Event: SESSION_CLOSED
```

#### 4.2.6 Notifier Service
```
NotifierWorker.cs
└── Event: NOTIFICATION_SENT (after Telegram message)
```

---

## 5. API ENDPOINTS SPECIFICATION

### 5.1 Timeline Events - `GET /api/timeline/events`

**Query Parameters**:
```
symbol=BTCUSDT                    # Required: Coin symbol
start_date=2026-04-01            # Optional: UTC date (YYYY-MM-DD)
end_date=2026-04-02              # Optional: UTC date (YYYY-MM-DD)
start_time=2026-04-02T10:00:00Z  # Optional: ISO 8601 datetime
end_time=2026-04-02T18:00:00Z    # Optional: ISO 8601 datetime
event_type=ORDER_PLACED          # Optional: Specific event type
event_category=TRADING           # Optional: PRICE_DATA, TRADING_SIGNAL, TRADING, RISK, etc.
source_service=Executor          # Optional: Service name filter
severity=INFO                    # Optional: DEBUG, INFO, WARNING, ERROR
limit=100                        # Optional: Default 100, Max 1000
offset=0                         # Optional: Pagination offset
sort_by=timestamp                # Optional: timestamp, created_at
sort_order=desc                  # Optional: asc or desc
```

**Response**:
```json
{
  "status": "success",
  "total": 450,
  "limit": 100,
  "offset": 0,
  "symbol": "BTCUSDT",
  "period": {
    "start": "2026-04-02T00:00:00Z",
    "end": "2026-04-02T23:59:59Z"
  },
  "data": [
    {
      "id": "ObjectId_1",
      "symbol": "BTCUSDT",
      "timestamp": "2026-04-02T10:30:45.123Z",
      "unix_timestamp": 1743667845123,
      "event_type": "ORDER_PLACED",
      "event_category": "TRADING",
      "source_service": "Executor",
      "severity": "INFO",
      "correlation_id": "CORR-XYZ123",
      "event_data": { /* ... */ },
      "metadata": { /* ... */ },
      "tags": ["buy", "entry"]
    },
    // ... more events
  ],
  "timestamp": "2026-04-02T12:30:00Z"
}
```

### 5.2 Timeline Summary - `GET /api/timeline/summary`

**Query Parameters**:
```
symbol=BTCUSDT                    # Required
date=2026-04-02                  # Optional: single date or daily summary
start_date=2026-04-01            # Optional
end_date=2026-04-02              # Optional
period=daily                     # Optional: daily or hourly
hour=10                          # Optional: if period=hourly
```

**Response**:
```json
{
  "status": "success",
  "symbol": "BTCUSDT",
  "summary": {
    "period": "daily",
    "date": "2026-04-02",
    "total_events": 156,
    "event_breakdown": {
      "ORDER_PLACED": 5,
      "ORDER_FILLED": 4,
      "SIGNAL_GENERATED": 24,
      "RISK_VALIDATION_APPROVED": 4,
      "RISK_VALIDATION_REJECTED": 2,
      "POSITION_OPENED": 4,
      "POSITION_CLOSED": 3
    },
    "trading_metrics": {
      "total_orders": 5,
      "filled_orders": 4,
      "win_rate": 0.75,
      "total_pnl": 450.75,
      "sessions_count": 6
    },
    "signals": {
      "total": 24,
      "strong": 16,
      "neutral": 6,
      "weak": 2,
      "accuracy": 0.92
    },
    "risk": {
      "validations_total": 6,
      "approvals": 4,
      "rejections": 2,
      "max_drawdown": 2.5
    },
    "market": {
      "regime": "TrendingUp",
      "avg_rsi": 62.5,
      "last_price": 45234.50,
      "high": 45500.00,
      "low": 44800.00
    }
  }
}
```

### 5.3 Timeline Export - `GET /api/timeline/export`

**Query Parameters**:
```
symbol=BTCUSDT                    # Required
start_date=2026-04-01            # Required for export
end_date=2026-04-02              # Required for export
format=csv                       # Optional: csv, json, excel
include_metadata=true            # Optional: Include event metadata
include_summary=true             # Optional: Include daily summary
```

**Response**: File download (CSV/JSON/XLSX)

### 5.4 Timeline Dashboard - `GET /api/timeline/dashboard`

**Query Parameters**:
```
symbol=BTCUSDT                    # Optional: if not provided, returns all symbols
days=7                           # Optional: last N days
```

**Response**:
```json
{
  "status": "success",
  "dashboard": {
    "overview": {
      "symbols_tracked": 5,
      "total_events_7d": 2450,
      "activity_score": 8.5
    },
    "coin_summaries": [
      {
        "symbol": "BTCUSDT",
        "last_7_days": {
          "total_events": 450,
          "orders": 35,
          "signals": 120,
          "positions": { "opened": 30, "closed": 28 }
        },
        "current_status": "Active",
        "last_event": "2026-04-02T10:30:45.123Z",
        "trending": "up"
      }
    ],
    "heatmap": [
      {
        "hour": 0,
        "activity_level": 2
      },
      // ... 24 hours
    ]
  }
}
```

### 5.5 Timeline Health - `GET /api/timeline/health`

```json
{
  "status": "healthy",
  "mongodb": {
    "connected": true,
    "latency_ms": 2,
    "collections": {
      "coin_events": { "count": 125440, "size_gb": 2.3 }
    }
  },
  "redis": {
    "connected": true,
    "latency_ms": 1,
    "subscribed_channels": 5
  },
  "event_processing": {
    "queue_size": 42,
    "events_per_second": 12.5,
    "avg_processing_time_ms": 15
  }
}
```

---

## 6. IMPLEMENTATION ROADMAP

### Phase 1: Foundation (Week 1-2)

**Tasks**:
1. Setup MongoDB cluster/instance
2. Create `TimelineLogger.Worker` project
3. Create `TimelineLogger.API` project
4. Design and implement event schema
5. Create MongoDB collections and indexes
6. Implement Redis subscriber
7. Implement event parser and validator

**Deliverables**:
- MongoDB initialized with collections
- Worker service running and listening to Redis
- Basic event parsing and storage

### Phase 2: Integration (Week 2-3)

**Tasks**:
1. Add event publishing to each existing service:
   - Ingestor
   - Analyzer
   - Strategy
   - RiskGuard
   - Executor
   - Notifier
2. Implement batch writer with queue management
3. Add correlation ID tracking
4. Implement metadata enrichment

**Deliverables**:
- All services publishing events
- Events flowing into MongoDB

### Phase 3: Query & Aggregation (Week 3-4)

**Tasks**:
1. Implement `TimelineQueryService`
2. Implement `TimelineAggregationService`
3. Create aggregation pipelines for daily/hourly summaries
4. Implement multi-service aggregation
5. Add caching layer (Redis for summaries)
6. Build `DailyAggregationWorker`

**Deliverables**:
- Fast query endpoints
- Daily summary generation
- Cache warming

### Phase 4: API Layer (Week 4-5)

**Tasks**:
1. Implement REST controllers
2. Add pagination and filtering
3. Add export functionality (CSV, JSON, XLSX)
4. Implement dashboard aggregation queries
5. Add authentication/authorization
6. Add OpenTelemetry metrics

**Deliverables**:
- Full REST API operational
- Dashboard data endpoints
- Export functionality

### Phase 5: Frontend Integration (Week 5-6)

**Tasks**:
1. Create React components for timeline display
2. Implement timeline filter UI
3. Create coin activity heatmap
4. Create event detail modal
5. Add timeline export UI
6. Internationalize (Vietnamese/English)

**Deliverables**:
- Frontend pages integrated
- Dashboard showing timeline data
- Export working from UI

### Phase 6: Optimization & Deployment (Week 6-7)

**Tasks**:
1. Add database indexing optimization
2. Implement retention policies
3. Add archival strategy
4. Performance testing
5. Load testing
6. Docker containerization
7. Monitoring and alerting

**Deliverables**:
- Production-ready service
- Monitoring configured
- Docker images ready

---

## 7. TECHNOLOGY STACK

| Component | Technology | Version | Rationale |
|-----------|-----------|---------|-----------|
| **Data Store** | MongoDB Community | 7.0+ | Flexible schema, TTL indexes, aggregation pipelines |
| **Message Queue** | Redis Pub/Sub | 7.0+ | Already in use, low latency |
| **Backend (Worker)** | .NET 10.0 | 10.0.103 | Consistency with existing stack |
| **Backend (API)** | .NET 10.0 (ASP.NET Core) | 10.0.103 | REST endpoints |
| **gRPC** | gRPC/Protobuf | Latest | Optional secondary protocol |
| **Serialization** | System.Text.Json | Built-in | Native .NET 10 support |
| **MongoDB Driver** | MongoDB.Driver | 3.0+ | Official, async-first support |
| **ORM** | None (raw LINQ) | — | Direct bson with MongoDB.Driver |
| **Caching** | Redis or in-memory | — | Summary caching |
| **Monitoring** | OpenTelemetry | Latest | Align with existing system |
| **Logging** | Serilog | Latest | Structured logging |
| **Export** | CsvHelper, OfficeOpenXml | Latest | CSV, Excel generation |
| **Frontend** | React, Vite, Tailwind | 18/5/3 | Existing stack |
| **Container** | Docker | Latest | Standard deployment |

---

## 8. DATA RETENTION & ARCHIVAL POLICY

### 8.1 Retention by Event Category

| Category | Retention | Archive | Notes |
|----------|-----------|---------|-------|
| PRICE_DATA | 7 days | No | High volume, low business value |
| ANALYSIS | 30 days | No | Debug info, not critical |
| TRADING_SIGNAL | 90 days | Yes | Important for backtesting |
| TRADING | 365 days | Yes | Full order history |
| POSITION | 365 days | Yes | Position closure history |
| RISK | 90 days | Yes | Risk decisions audit trail |
| LIQUIDATION | 365 days | Yes | Critical events |
| SESSION | 365 days | Yes | Session records |
| NOTIFICATION | 90 days | No | Notification sent status |
| MAINTENANCE | 180 days | No | System housekeeping |

### 8.2 Archival Strategy

```
1. Monthly rotation to archive collection
2. Compress historical data after 1 year
3. Store in S3/blob storage (future)
4. Keep queryable index for 1 year
5. Auto-delete after retention period via TTL index
```

---

## 9. DEPLOYMENT ARCHITECTURE

### 9.1 Docker Compose Addition

**Services**:
```yaml
mongodb:
  image: mongo:7.0
  ports:
    - "27017:27017"
  environment:
    MONGO_INITDB_ROOT_USERNAME: ${MONGO_USERNAME}
    MONGO_INITDB_ROOT_PASSWORD: ${MONGO_PASSWORD}
    MONGO_INITDB_DATABASE: cryptotrading_timeline
  volumes:
    - mongo_data:/data/db
    - ./init-mongo.js:/docker-entrypoint-initdb.d/init-mongo.js:ro

timelinelogger:
  image: cryptotrading/timelinelogger:latest
  depends_on:
    - mongodb
    - redis
    - postgres
  ports:
    - "5016:5016"    # gRPC
    - "5096:5096"    # HTTP API
  environment:
    ASPNETCORE_URLS: "http://+:5096;grpc://+:5016"
    MongoDB__ConnectionString: "mongodb://${MONGO_USERNAME}:${MONGO_PASSWORD}@mongodb:27017/cryptotrading_timeline?authSource=admin"
    Redis__ConnectionString: "redis:6379"
    TimelineLogger__EventRetentionDays: 90
    TimelineLogger__BatchSize: 1000
```

### 9.2 Environment Variables

```env
# MongoDB
MONGO_USERNAME=timeline_user
MONGO_PASSWORD=strongpassword
MONGO_DATABASE=cryptotrading_timeline

# Timeline Service
TIMELINE_EVENT_RETENTION_DAYS=90
TIMELINE_BATCH_SIZE=1000
TIMELINE_BATCH_TIMEOUT_MS=5000
TIMELINE_ENABLE_COMPRESSION=false
TIMELINE_CACHE_TTL_MINUTES=60

# Redis
REDIS_TIMELINE_CHANNEL_PATTERN=coin:*:log

# Observability
OTEL_EXPORTER_OTLP_ENDPOINT=http://jaeger:4317
TIMELINE_METRICS_ENABLED=true
TIMELINE_DEBUG_LOG_EVENTS=false
```

---

## 10. MONITORING & OBSERVABILITY

### 10.1 Key Metrics

```
- Events ingested per second
- Event processing latency (p50, p95, p99)
- MongoDB write latency
- Query response time
- Queue depth
- Error rate by event type
- Cache hit ratio
```

### 10.2 Alerts

```
- Subscriber disconnection
- MongoDB connection failure
- Event processing delay > 1min
- Queue backlog > 10,000 events
- Query latency > 5s
- Error rate > 1%
```

---

## 11. SECURITY CONSIDERATIONS

1. **Authentication**: Bearer token validation on API endpoints
2. **Authorization**: Role-based access (admin can export, users can view)
3. **Encryption**: TLS for MongoDB and Redis connections
4. **Data Sensitivity**: PII filtering in exported data
5. **Audit**: All API access logged to separate audit stream
6. **Rate Limiting**: API endpoint rate limits (100 req/min per user)

---

## 12. TESTING STRATEGY

### 12.1 Unit Tests
- Event parser validation
- Data model serialization
- Query builder logic
- Export formatting

### 12.2 Integration Tests
- Redis subscriber message handling
- MongoDB write/read operations
- Aggregation pipeline correctness
- Full event flow (publish → store → query)

### 12.3 Performance Tests
- 1000+ events/sec throughput
- Sub-100ms query response
- Bulk export (30 days data)
- Concurrent user load

### 12.4 End-to-End Tests
- Service communication
- Cross-service event publishing
- Dashboard data freshness
- Retention policy execution

---

## 13. FUTURE ENHANCEMENTS

1. **Machine Learning**: Anomaly detection in event patterns
2. **Real-time Dashboard**: WebSocket support for live updates
3. **Event Replay**: Ability to replay historical scenarios
4. **Advanced Analytics**: Correlation analysis between events
5. **Mobile App**: Native mobile timeline viewer
6. **Webhook Integration**: Third-party system notifications
7. **Event Streaming**: Kafka integration for enterprise deployments
8. **Distributed Tracing**: Full request tracing across services

---

## 14. INTEGRATION CHECKLIST

Services to modify for event publishing:

- [ ] Ingestor.Worker - PriceTickPersistenceWorker
- [ ] Analyzer.Worker - SignalAnalyzerWorker, FundingRateFetcherWorker
- [ ] Strategy.Worker - Worker loop
- [ ] RiskGuard.API - RiskValidationEngine
- [ ] Executor.API - OrderExecutionService, PositionLifecycleManager, LiquidationOrchestrator
- [ ] Notifier.Worker - NotifierWorker
- [ ] Frontend - Add TimelineEventsPage component
- [ ] Gateway.API - Add proxy routes to TimelineLogger.API

---

## 15. APPENDIX: SAMPLE EVENT STRUCTURES

### Example: ORDER_PLACED Event

```json
{
  "correlation_id": "CORR-ORD-2026-04-02-001",
  "timestamp": "2026-04-02T10:30:45.123Z",
  "source_service": "Executor",
  "event_type": "ORDER_PLACED",
  "symbol": "BTCUSDT",
  "session_id": "SESSION-2026-04-02-0",
  "severity": "INFO",
  "payload": {
    "order_id": "ORD-2026-04-02-001",
    "binance_order_id": 1234567890,
    "side": "BUY",
    "order_type": "Limit",
    "quantity": 0.5,
    "entry_price": 45000.00,
    "stop_loss": 44500.00,
    "take_profit": 46000.00,
    "time_in_force": "GTC",
    "placed_at": "2026-04-02T10:30:45.123Z"
  },
  "metadata": {
    "market_regime": "TrendingUp",
    "current_rsi": 62.5,
    "ema9": 44950.00,
    "ema21": 44800.00,
    "bollinger_upper": 46200.00,
    "bollinger_middle": 45000.00,
    "bollinger_lower": 43800.00,
    "atr": 450.00,
    "funding_rate": 0.0001,
    "bid_ask_spread": 5.50
  },
  "tags": ["buy", "entry", "strong_signal", "limit_order"]
}
```

### Example: SIGNAL_GENERATED Event

```json
{
  "correlation_id": "CORR-SIG-2026-04-02-001",
  "timestamp": "2026-04-02T10:30:00.000Z",
  "source_service": "Analyzer",
  "event_type": "SIGNAL_GENERATED",
  "symbol": "BTCUSDT",
  "severity": "INFO",
  "payload": {
    "signal_id": "SIG-2026-04-02-001",
    "signal_strength": "Strong",
    "side": "BUY",
    "entry_level": 44950.00,
    "rsi": 62.5,
    "rsi_status": "overbought_entry",
    "ema9": 44950.00,
    "ema21": 44800.00,
    "ema_cross": "bullish",
    "bollinger_band": "squeeze_break",
    "bollinger_position": "middle",
    "adx": 35.2,
    "adx_status": "strong_trend",
    "atr": 450.00,
    "volume_signal": "high_confidence",
    "market_regime": "TrendingUp",
    "funding_rate": 0.0001,
    "funding_rate_safe": true,
    "confidence_score": 0.92
  },
  "metadata": {
    "lookback_periods": 14,
    "data_points_used": 120,
    "last_signal": "2026-04-02T09:00:00.000Z",
    "signals_today": 14
  },
  "tags": ["strong_buy", "ema_cross", "trend_following", "bb_squeeze_break"]
}
```

### Example: RISK_VALIDATION_REJECTED Event

```json
{
  "correlation_id": "CORR-RISK-2026-04-02-001",
  "timestamp": "2026-04-02T10:31:00.000Z",
  "source_service": "RiskGuard",
  "event_type": "RISK_VALIDATION_REJECTED",
  "symbol": "BTCUSDT",
  "session_id": "SESSION-2026-04-02-0",
  "severity": "WARNING",
  "payload": {
    "order_request_id": "REQ-2026-04-02-001",
    "rejection_rule": "CooldownRule",
    "rejection_reason": "Symbol BTCUSDT in cooldown period. Last order 2 minutes ago. Required: 30 seconds",
    "cooldown_ends_at": "2026-04-02T10:32:00.000Z",
    "rejected_at": "2026-04-02T10:31:00.000Z",
    "rules_evaluated": 3,
    "rules_passed": 2,
    "all_rules_checked": false,
    "requested_order": {
      "symbol": "BTCUSDT",
      "side": "BUY",
      "quantity": 0.5,
      "price": 45000.00
    }
  },
  "metadata": {
    "account_status": "active",
    "daily_pnl": 250.00,
    "position_count": 1
  },
  "tags": ["rejected", "cooldown", "symbol_limit"]
}
```

---

**Document Version**: 1.0  
**Last Updated**: April 2, 2026  
**Next Review**: After Phase 2 completion
