# Phase 5: Observability & Hardening

## Overview
Phase 5 focuses on implementing comprehensive observability across the trading system, including distributed tracing, metrics collection, and dashboards. This enables real-time monitoring of system health, order execution performance, and revenue tracking.

## Observability Stack

```
Applications (Executor, RiskGuard, Strategy)
    ↓
OpenTelemetry SDK (tracing + metrics)
    ↓
    ├─→ Prometheus (time-series metrics)
    │    ↓
    │    └─→ Grafana (visualization + alerts)
    │
    ├─→ Jaeger (distributed tracing)
    │
    └─→ Console Exporter (local development logs)
```

## 1. OpenTelemetry Integration

### Current Implementation
- **Executor.API**: Custom `OrderExecutionMetrics` class recording:
  - `executor.orders.placed.total` - Counter for all orders
  - `executor.orders.rejected.total` - Counter for rejected orders
  - `executor.orders.filled.total` - Counter for successful fills
  - `executor.order.latency.ms` - Histogram of execution latency
  - `executor.fill.price.deviation.percent` - Slippage tracking
  - `executor.orders.active` - Current active orders gauge
  - `executor.audit.events.published.total` - Audit trail publishing

- **Distributed Tracing**: Configured with console exporter for development

### Adding to Other Services

To add observability to RiskGuard.API and Strategy.Worker:

1. Add NuGet packages (already in Directory.Packages.props):
```bash
dotnet add package OpenTelemetry
dotnet add package OpenTelemetry.Exporter.Console
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Extensions.Hosting
```

2. Update Program.cs:
```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddConsoleExporter();
    });
```

3. Create service-specific metrics classes similar to `OrderExecutionMetrics.cs`

## 2. Prometheus Setup

### Configuration File
Located at: `infrastructure/prometheus.yml`

Scrapers configured for:
- **Executor.API**: port 9091, metrics endpoint
- **RiskGuard.API**: port 9092, metrics endpoint
- **Strategy.Worker**: port 9093, metrics endpoint

### Metric Types Exposed
- **Counter**: `executor_orders_placed_total`, `executor_orders_filled_total`
- **Gauge**: `executor_orders_active`
- **Histogram**: `executor_order_latency_ms`, `executor_fill_price_deviation_percent`

## 3. Grafana Setup

### Default Credentials
- **URL**: http://localhost:3000
- **Username**: admin
- **Password**: admin (configured in docker-compose)

### Pre-configured Dashboards

#### Trading System - Order Execution
**Path**: `infrastructure/grafana/provisioning/dashboards/trading-order-execution.json`

**Panels**:
1. **Order Placement Rate** (5m rolling)
   - Measures orders/sec being sent to Executor
   - Early indicator of signal generation capacity

2. **Order Execution Latency** (p95/p99)
   - Tracks gRPC round-trip + validation + persistence time
   - Alert threshold: p99 > 100ms

3. **Orders Filled vs Rejected** (5m window)
   - Shows order acceptance rate
   - Identifies when risk rules are too strict

4. **Audit Trail Publishing Success Rate**
   - Ensures orders are being persisted to Redis Streams
   - Should be 100%

## 4. Starting the Observability Stack

### Quick Start
```bash
cd infrastructure

# Start core services (Redis, PostgreSQL)
docker-compose up -d

# Start observability tools (Prometheus, Grafana, Jaeger)
docker-compose -f docker-compose.yml -f docker-compose-observability.yml up -d
```

### Access Points
- **Grafana**: http://localhost:3000
- **Prometheus**: http://localhost:9090
- **Jaeger**: http://localhost:16686

### Verify Services
```bash
# Check all running containers
docker ps --filter "name=trading" --format "table {{.Names}}\t{{.Status}}"

# Expected output:
# trading_postgres       Up 2 minutes
# trading_redis          Up 2 minutes
# trading_prometheus     Up 30 seconds
# trading_grafana        Up 20 seconds
# trading_jaeger         Up 25 seconds
```

## 5. Application Metrics Endpoints

Each service exposes metrics on a dedicated port:

| Service | Port | Endpoint | Format |
|---------|------|----------|--------|
| Executor.API | 9091 | `/metrics` | Prometheus |
| RiskGuard.API | 9092 | `/metrics` | Prometheus |
| Strategy.Worker | 9093 | `/metrics` | Prometheus |

### Testing Metrics Endpoint
```bash
# Executor metrics
curl http://localhost:9091/metrics

# Prometheus scrape status
curl http://localhost:9090/api/v1/targets
```

## 6. Key Metrics to Monitor

### Order Execution Pipeline
```
executor_orders_placed_total 
  ↓ (riskguard.orders.rejected_total)
executor_orders_filled_total
  ↓ (audit trail)
executor_audit_events_published_total
```

### Performance Indicators
- **Latency**: If p99 > 100ms, check PostgreSQL connection pool and Redis latency
- **Rejection Rate**: If > 10%, review Risk Rules in RiskGuard
- **Audit Failures**: Should be 0; indicates database persistence issues

### Slippage & Fill Quality
- `executor_fill_price_deviation_percent` tracks paper trading slippage
- Useful for backtesting simulator accuracy

## 7. Creating Custom Alerts

### Example: High Order Rejection Rate
```yaml
# Add to prometheus.yml under rule_files
groups:
  - name: trading_alerts
    rules:
      - alert: HighOrderRejectionRate
        expr: |
          (increase(executor_orders_rejected_total[5m]) / 
           increase(executor_orders_placed_total[5m])) > 0.2
        for: 5m
        annotations:
          summary: "Order rejection rate > 20% for 5 minutes"
          description: "Check RiskGuard rules configuration"
```

## 8. Prometheus Query Examples

### Orders Per Symbol
```promql
sum by (symbol) (rate(executor_orders_filled_total[5m]))
```

### Average Latency by Symbol
```promql
avg by (symbol) (rate(executor_order_latency_ms_sum[5m]) / 
                 rate(executor_order_latency_ms_count[5m]))
```

### Filled Quantity Over Time
```promql
increase(executor_orders_filled_total[1h])
```

### Paper vs Live Trading Ratio
```promql
sum by (is_paper) (increase(executor_orders_filled_total[5m]))
```

## 9. Structured Logging Integration

### Current State
- Services use `ILogger<T>` from Microsoft.Extensions.Logging
- OpenTelemetry console exporter outputs to stdout

### Recommendation: Serilog Integration
For production, configure Serilog with structured fields:

```csharp
var logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "{Timestamp:o} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Executor.API")
    .Enrich.WithProperty("Version", "1.0.0")
    .CreateLogger();
```

## 10. Load Testing Scenario

### Objective
Verify the system can handle 1000+ signals/second without degradation

### Test Setup
```bash
# Generate synthetic signals
cd scripts
dotnet run --project LoadTestSignalGenerator

# Configure:
# - Symbols: BTCUSDT, ETHUSDT, BNBUSDT
# - Rate: 500-1000 signals/sec
# - Duration: 5 minutes
# - Observe Grafana dashboards
```

### Success Criteria
- p99 latency < 100ms
- Rejection rate < 5%
- Audit trail 100% publishing
- No dropped signals

## 11. Native AOT Compilation

### Status
Phase 5 includes validation that all services compile with Native AOT.

### Why?
- **Startup Time**: <100ms vs 2-5s for JIT
- **Memory**: 30-50% reduction
- **Distribution**: Single executable, no .NET runtime required

### Validation
```bash
# Publish as Native AOT
dotnet publish -c Release -r linux-x64 /p:PublishAot=true

# Expected size: ~150-200MB per service
ls -lh bin/Release/net8.0/linux-x64/publish/
```

### Known Issues
- Reflection-based code may require runtime JSON configuration
- Generic type inference limitations

## 12. Troubleshooting Observability

### Prometheus Scrape Failures
```bash
# Check targets
curl http://localhost:9090/api/v1/targets | jq '.data.activeTargets[] | {.labels.job, .lastScrapeTime, .health}'

# Check logs
docker logs trading_prometheus | tail -50
```

### Missing Metrics in Grafana
1. Verify OpenTelemetry meter is registered: `AddMeter("ServiceName.Metrics")`
2. Check Prometheus scrape config includes the service
3. Query directly in Prometheus: http://localhost:9090/graph

### High Latency Detection
```promql
# Find slow requests
histogram_quantile(0.99, rate(executor_order_latency_ms_bucket[5m])) > 100
```

## 13. Next Steps

Phase 6 will build on this observability foundation to create:
- Web dashboard API endpoints
- Real-time WebSocket feeds
- Trade analysis and reporting

---

**Status**: Phase 5 Foundation ✅ 
- OpenTelemetry integrated with Executor.API
- Prometheus + Grafana stack configured
- Custom metrics for order execution
- Ready to scale observability to other services

**Estimated Phase 5 Completion**: After documenting structured logging integration and validating load testing scenario
