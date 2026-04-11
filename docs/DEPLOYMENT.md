# Docker Compose Deployment Guide

## Updated docker-compose.yml Overview

The `docker-compose.yml` has been updated to include all 7 microservices plus supporting infrastructure:

### Services Included

1. **Redis** (6379) - Message broker & pub/sub
2. **PostgreSQL/TimescaleDB** (5433) - Time-series database
3. **DataIngestor** (5010) - Binance WebSocket feed
4. **Analyzer** (5011) - Technical indicator calculations
5. **Strategy** (5012) - Trading decision engine
6. **RiskGuard** (5013) - Order validation (gRPC)
7. **OrderExecutor** (5014) - Order placement (gRPC)
8. **Notifier** (5015) - Telegram alerts worker
9. **Gateway** (5000) - Dashboard & reverse proxy

## Prerequisites

- Docker and Docker Compose installed
- 4GB+ RAM available
- 10GB+ disk space
- Network connectivity (for Binance API)

## Pre-Deploy Checklist (Ledger Stream Safety)

Run this checklist before first deploy on a new environment, or before deploying FinancialLedger/Executor updates that affect ledger events.

- [ ] Apply required ledger migration for cashflow entry types:

```powershell
Set-Location d:\Code\CryptoAlgorithmicTrading
Get-Content -Raw .\scripts\add-ledger-entry-cashflow-types.sql | docker compose -f .\infrastructure\docker-compose.yml exec -T postgres psql -U trader -d cryptotrading -v ON_ERROR_STOP=1
```

- [ ] Verify the constraint includes both `BUY_CASH_OUT` and `SELL_CASH_IN`:

```powershell
Set-Location d:\Code\CryptoAlgorithmicTrading\infrastructure
docker compose exec -T postgres psql -U trader -d cryptotrading -c "SELECT conname, pg_get_constraintdef(c.oid) AS def FROM pg_constraint c JOIN pg_class t ON c.conrelid = t.oid WHERE t.relname = 'ledger_entries' AND c.contype = 'c';"
```

- [ ] Confirm no consume errors from FinancialLedger:

```powershell
Set-Location d:\Code\CryptoAlgorithmicTrading\infrastructure
docker compose logs --since 5m financialledger | Select-String -Pattern "ledger_entries_type_check|Error while consuming ledger events stream"
```

- [ ] If pending stream entries exist, replay safely with one command:

```powershell
Set-Location d:\Code\CryptoAlgorithmicTrading
powershell -ExecutionPolicy Bypass -File .\scripts\replay-ledger-pending.ps1
```

Expected result after replay:
- `XPENDING ledger:events financial-ledger` returns `0`.
- FinancialLedger logs stop showing `ledger_entries_type_check` errors.

## Step-by-Step Deployment

### 1. Prepare Environment

```powershell
cd infrastructure

# Create .env file from template
cp .env.template .env

# Edit .env with your actual credentials
notepad .env
```

### 2. Verify Configuration

```powershell
# Validate docker-compose.yml syntax
docker compose config

# Check for any errors
# If no errors shown, configuration is valid
```

### 3. Build Service Images

```powershell
cd ..  # Back to project root

# Build all service images (this will take several minutes)
docker compose build

# Or build specific service
docker compose build ingestor
```

Monitor the build progress. Each service builds independently.

### 4. Start Infrastructure Services First

```powershell
cd infrastructure

# Start Redis and PostgreSQL
docker compose up -d redis postgres

# Wait for them to be healthy
Start-Sleep -Seconds 5
docker compose ps
```

Verify both show "healthy" status.

### 5. Start All Services

```powershell
# Start all remaining services
docker compose up -d

# Check status
docker compose ps
```

All services should show "Up" status.

### 6. Verify Service Connectivity

```powershell
# Test Redis
docker compose exec redis redis-cli ping
# Should respond: PONG

# Test PostgreSQL
docker compose exec postgres psql -U trader -d cryptotrading -c "SELECT 1"
# Should respond: (1 row)

# Check application logs
docker compose logs ingestor --tail=20
docker compose logs notifier --tail=20
```

### 7. Monitor Services

```powershell
# View all logs in real-time
docker compose logs -f

# View specific service logs
docker compose logs -f ingestor
docker compose logs -f strategy
docker compose logs -f gateway

# Exit logs: Ctrl+C
```

## Service Health Checks

Each service includes health checks. To monitor:

```powershell
# View health status
docker compose ps

# Manually verify service health
Invoke-WebRequest http://localhost:5010/health
Invoke-WebRequest http://localhost:5011/health
Invoke-WebRequest http://localhost:5012/health
```

## Common Operations

### Stop All Services

```powershell
docker compose down

# Keep volumes (data persists)
# Volume will remain: postgres_data
```

### Remove Everything (Including Data)

```powershell
# WARNING: This deletes the database!
docker compose down -v
```

### Restart Specific Service

```powershell
# Restart analyzer
docker compose restart analyzer

# Force recreate (useful after code changes)
docker compose up -d --force-recreate analyzer
```

### Rebuild After Code Changes

```powershell
# Rebuild specific service
docker compose build analyzer

# And restart it
docker compose up -d analyzer
```

### View Resource Usage

```powershell
docker stats
```

### Access Database

```powershell
# PostgreSQL CLI
docker compose exec postgres psql -U trader -d cryptotrading

# Example queries:
# \dt                    - List all tables
# SELECT * FROM active_symbols;
# SELECT * FROM orders;
# \q                     - Quit
```

### Access Redis

```powershell
# Redis CLI
docker compose exec redis redis-cli

# Example commands:
# KEYS *                 - List all keys
# GET price:BTCUSDT      - Get a value
# PSUBSCRIBE price:*     - Subscribe to prices
# SUBSCRIBE system:events
# MONITOR                - Show all commands
```

### View Service Logs

```powershell
# Last 100 lines
docker compose logs --tail=100 gateway

# Follow in real-time
docker compose logs -f ingestor

# Last N minutes
docker compose logs --since 5m strategy
```

## Docker Compose File Structure

Each service in docker-compose.yml includes:

```yaml
service_name:
  build:
    context: ..
    dockerfile: path/to/Dockerfile
  container_name: unique_name
  depends_on:
    - service1
    - service2
  environment:
    - VAR1=value1
    - VAR2=value2
  ports:
    - "host_port:container_port"
  restart: unless-stopped
  networks:
    - trading_network
  logging:
    driver: json-file
```

### Key Features

- **depends_on** - Ensures services start in correct order
- **healthcheck** - Verifies service is ready
- **environment** - Configuration from .env file
- **networks** - Isolated internal network
- **logging** - Limits log file size to prevent disk space issues

## Troubleshooting

### Service Won't Start

```powershell
# Check logs for errors
docker compose logs service_name

# Verify dependencies are healthy
docker compose ps

# Rebuild service
docker compose build --no-cache service_name
docker compose up -d service_name
```

### Port Already in Use

```powershell
# Change port mapping in docker-compose.yml
# Find the port and change it
# Before: "5010:5010"
# After:  "5020:5010"  (host:container)

# Then restart
docker compose up -d
```

### Database Connection Issues

```powershell
# Check PostgreSQL is running
docker compose ps postgres

# Verify connection string matches .env
# Test connection
docker compose exec postgres psql -U trader -d cryptotrading -c "SELECT NOW()"
```

### Memory/Disk Space Issues

```powershell
# Check disk usage
docker system df

# Clean up unused images
docker system prune

# Remove dangling volumes
docker volume prune

# Clear build cache
docker builder prune
```

## Performance Tuning

### PostgreSQL Configuration

```powershell
# Increase connection limit (if needed)
# In docker-compose.yml, add to postgres environment:
# - POSTGRES_INIT_ARGS=-c max_connections=200
```

### Redis Configuration

```powershell
# Add memory limit
# In docker-compose.yml redis service:
# command: redis-server --maxmemory 256mb --maxmemory-policy allkeys-lru
```

## Production Deployment

For production deployment beyond Docker Compose:

1. Use **Kubernetes** for orchestration
2. Use **Docker Swarm** for clustering
3. Use **Cloud platforms**: AWS ECS, Azure Container Instances, GCP Cloud Run
4. Set up proper **logging**: ELK Stack, Datadog, New Relic
5. Configure **monitoring**: Prometheus + Grafana
6. Set up **CI/CD**: GitHub Actions, GitLab CI
7. Use **secrets management**: HashiCorp Vault, AWS Secrets Manager

## Next Steps

1. **Verify all services are running:**
   ```powershell
   docker compose ps
   ```

2. **Test data flow:**
   - Monitor Redis channels: `docker compose exec redis redis-cli PSUBSCRIBE "price:*"`
   - Check database: `docker compose exec postgres psql -U trader -d cryptotrading -c "SELECT COUNT(*) FROM price_ticks"`

3. **Add Binance API keys** (if running live)
   - Edit .env: `BINANCE_API_KEY=your_key`
   - Restart ingestor: `docker compose restart ingestor`

4. **Monitor Gateway dashboard** at http://localhost:5000

5. **Check Notifier logs** for Telegram alerts

## Support & Documentation

- [Docker Compose Docs](https://docs.docker.com/compose/)
- [Dockerfile Best Practices](https://docs.docker.com/develop/develop-images/dockerfile_best-practices/)
- [TimescaleDB Docker](https://docs.timescale.com/install/latest/docker/)
- [Redis Docker Hub](https://hub.docker.com/_/redis)

---

**Last Updated:** See git history for latest changes
**Status:** All services configured and ready for deployment
