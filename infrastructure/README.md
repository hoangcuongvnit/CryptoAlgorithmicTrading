# Infrastructure & Deployment Guide

## Docker Compose Architecture

The `docker-compose.yml` orchestrates all services with proper dependency management.

### Service Dependencies

```
┌─────────────────────────────────────────────────────────────┐
│  PostgreSQL (TimescaleDB)     Redis                          │
│  Port: 5433                    Port: 6379                    │
└──────────────────────┬────────────────────┬──────────────────┘
                       │                    │
        ┌──────────────┼────────────────────┼──────────────┐
        │              │                    │              │
    [Ingestor]    [Analyzer]           [Executor]     [RiskGuard]
    Port: 5010     Port: 5011          Port: 5014      Port: 5013
        │              │                    │              │
        └──────────────┴────────┬───────────┴──────────────┘
                                │
                         [Strategy Brain]
                          Port: 5012
                                │
        ┌───────────────────────┴────────────────────┐
        │                                            │
    [Notifier]                                 [Gateway/Dashboard]
    Port: 5015                                  Port: 5000
```

## Quick Start

### 1. Create .env File (IMPORTANT)

```bash
cd infrastructure
cp .env.template .env
```

**Edit `.env` with your values:**
```env
# Add your actual API keys and tokens
BINANCE_API_KEY=your_key_here
BINANCE_API_SECRET=your_secret_here
TELEGRAM_BOT_TOKEN=your_bot_token_here
TELEGRAM_CHAT_ID=123456789
```

### 2. Start Infrastructure Only (Redis + PostgreSQL)

```bash
cd infrastructure
docker compose up -d redis postgres
```

**Verify services are healthy:**
```bash
docker compose ps
docker compose logs redis postgres
```

### 3. Build All Service Images

```bash
# From project root
docker compose build

# Or build specific service
docker compose build ingestor
```

### 4. Start All Services

```bash
cd infrastructure
docker compose up -d
```

**Check service status:**
```bash
docker compose ps
```

Expected output:
```
NAME                COMMAND                  SERVICE      STATUS       PORTS
trading_redis       redis-server             redis        Up (healthy) 0.0.0.0:6379->6379/tcp
trading_postgres    /entrypoint.sh postgres  postgres     Up (healthy) 0.0.0.0:5433->5433/tcp
trading_ingestor    dotnet Ingestor.Worker   ingestor     Up           0.0.0.0:5010->5010/tcp
trading_analyzer    dotnet Analyzer.Worker   analyzer     Up           0.0.0.0:5011->5011/tcp
trading_strategy    dotnet Strategy.Worker   strategy     Up           0.0.0.0:5012->5012/tcp
trading_riskguard   dotnet RiskGuard.API     riskguard    Up           0.0.0.0:5013->5013/tcp
trading_executor    dotnet Executor.API      executor     Up           0.0.0.0:5014->5014/tcp
trading_notifier    dotnet Notifier.Worker   notifier     Up           0.0.0.0:5015->5015/tcp
trading_gateway     dotnet Gateway.API       gateway      Up           0.0.0.0:5000->5000/tcp
```

### 5. Monitor Service Logs

```bash
# All services
docker compose logs -f

# Specific service
docker compose logs -f ingestor
docker compose logs -f analyzer
docker compose logs -f strategy
docker compose logs -f notifier

# Last 100 lines
docker compose logs --tail=100 ingestor
```

### 6. Stop All Services

```bash
# Stop but keep volumes (database data persists)
docker compose down

# Completely remove all data
docker compose down -v
```

## Service Endpoints

| Service | Type | Endpoint | Port | Notes |
|---------|------|----------|------|-------|
| **Gateway** | HTTP | http://localhost:5000 | 5000 | Dashboard & API proxy |
| **DataIngestor** | HTTP | http://localhost:5010/health | 5010 | Price feed worker |
| **Analyzer** | HTTP | http://localhost:5011/health | 5011 | Signal calculator |
| **Strategy** | HTTP | http://localhost:5012/health | 5012 | Trade decision engine |
| **RiskGuard** | gRPC | localhost:5013 | 5013 | Order validation |
| **OrderExecutor** | gRPC | localhost:5014 | 5014 | Order execution |
| **Notifier** | HTTP | http://localhost:5015/health | 5015 | Telegram alerts |
| **Redis** | TCP | localhost:6379 | 6379 | Message broker |
| **PostgreSQL** | TCP | localhost:5433 | 5433 | Time-series database |

## Database Access

### Direct PostgreSQL Access

```bash
# Using docker compose
docker compose exec postgres psql -U trader -d cryptotrading

# List tables
\dt

# Query price ticks
SELECT symbol, price, timestamp FROM price_ticks ORDER BY timestamp DESC LIMIT 10;

# Query recent orders
SELECT symbol, side, filled_price, success FROM orders ORDER BY time DESC LIMIT 10;

# Exit
\q
```

### Connection String

```
Host=localhost;Port=5433;Database=cryptotrading;Username=trader;Password=strongpassword
```

## Redis Access

### Direct Redis CLI

```bash
# Connect
docker compose exec redis redis-cli

# Subscribe to all price ticks
PSUBSCRIBE "price:*"

# Subscribe to signals
PSUBSCRIBE "signal:*"

# Monitor all activity
MONITOR

# Get list of keys
KEYS *

# Get specific symbol price
GET "price:BTCUSDT"

# Exit
exit
```

### Redis Streams (Audit Log)

```bash
# Read all trades audit
XRANGE trades:audit - +

# Read last 10 trades
XREVRANGE trades:audit + - COUNT 10

# Read trades after specific timestamp
XREAD COUNT 5 STREAMS trades:audit 0
```

## Troubleshooting

### Services Won't Start

```bash
# Check specific service logs
docker compose logs postgres
docker compose logs redis
docker compose logs ingestor

# Check if ports are already in use
netstat -an | findstr :5000
netstat -an | findstr :5433
netstat -an | findstr :6379
```

### PostgreSQL Connection Issues

```bash
# Restart postgres
docker compose restart postgres

# Check postgres health
docker compose exec postgres pg_isready -U trader -d cryptotrading

# View postgres logs
docker compose logs postgres --tail=50
```

### Redis Connection Issues

```bash
# Restart redis
docker compose restart redis

# Test redis connection
docker compose exec redis redis-cli ping
# Expected response: PONG
```

### Service Health Checks Failing

```bash
# Rebuild service image
docker compose build --no-cache ingestor

# Restart service
docker compose restart ingestor

# View health check logs
docker compose ps
# Look for "(unhealthy)" status
```

### Docker Build Fails

```bash
# Clear build cache
docker compose build --no-cache

# Check if Docker has enough space
docker system df

# Clean up unused images
docker system prune
```

## Development vs Production

### Using docker-compose for Development

```bash
# Use local source code instead of building
docker compose -f docker-compose.yml -f docker-compose.dev.yml up
```

### Building for Production

```bash
# Build all images
docker compose build --no-cache

# Push to registry (optional)
docker tag trading_ingestor myregistry/trading_ingestor:latest
docker push myregistry/trading_ingestor:latest
```

## Performance Monitoring

### Check Container Resource Usage

```bash
docker stats
```

### Monitor Redis Memory

```bash
docker compose exec redis redis-cli info memory
```

### Monitor PostgreSQL Connections

```bash
docker compose exec postgres psql -U trader -d cryptotrading \
  -c "SELECT datname, count(*) FROM pg_stat_activity GROUP BY datname;"
```

## Backup and Recovery

### Backup PostgreSQL Data

```bash
# Create backup
docker compose exec postgres pg_dump -U trader -d cryptotrading > backup.sql

# Restore from backup
docker compose exec -T postgres psql -U trader -d cryptotrading < backup.sql
```

### Backup Redis Data

```bash
# Redis automatically saves to disk
docker cp trading_redis:/data/dump.rdb ./dump.rdb

# Restore by copying back
docker cp ./dump.rdb trading_redis:/data/dump.rdb
```

## Common Commands

```bash
# View specific service logs
docker compose logs ingestor -f --tail=20

# Restart all services
docker compose restart

# Restart specific service
docker compose restart strategy

# Execute command in service
docker compose exec ingestor ls -la

# View resource usage
docker compose stats

# Validate docker-compose.yml
docker compose config

# Pull latest images
docker compose pull
```

## Security Considerations

1. **Change default PostgreSQL password** (in .env)
   ```env
   POSTGRES_PASSWORD=your_strong_password_here
   ```

2. **Use secrets for sensitive data**
   - Never commit .env with real credentials
   - Use Docker secrets in production

3. **Network isolation**
   - All services use internal trading_network
   - Only specific ports exposed

4. **API Keys**
   - Store in environment variables
   - Never commit to version control
   - Rotate regularly

## Help & Resources

- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [TimescaleDB Documentation](https://docs.timescale.com/)
- [Redis Documentation](https://redis.io/documentation)
- [.NET Runtime Documentation](https://docs.microsoft.com/dotnet/)
