# Docker Compose Quick Reference

## ✅ Configuration Status

- **docker-compose.yml**: ✅ Valid
- **All 9 services defined**: ✅ Complete
- **Dockerfiles**: ✅ Created (7 services)
- **.env template**: ✅ Ready
- **Health checks**: ✅ Configured

## 🚀 Quick Start Commands

### Initialize & Start

```powershell
# 1. Prepare environment
cd infrastructure
cp .env.template .env

# 2. Edit .env with your credentials (IMPORTANT!)
# - BINANCE_API_KEY
# - BINANCE_API_SECRET
# - TELEGRAM_BOT_TOKEN
# - TELEGRAM_CHAT_ID

# 3. Build service images
cd ..
docker compose build

# 4. Start all services
cd infrastructure
docker compose up -d

# 5. Verify status
docker compose ps
```

### Monitor & Debug

```powershell
# View all logs in real-time
docker compose logs -f

# View specific service
docker compose logs -f ingestor
docker compose logs -f gateway

# Check service health
docker compose ps

# Execute command in service
docker compose exec ingestor dotnet
```

### Stop & Cleanup

```powershell
# Stop all services (keep data)
docker compose down

# Remove everything including data
docker compose down -v

# Restart all services
docker compose restart

# Restart specific service
docker compose restart strategy
```

## 📊 Service Ports

| Service | Port | Type | Purpose |
|---------|------|------|---------|
| Gateway | 5000 | HTTP | Dashboard & reverse proxy |
| DataIngestor | 5010 | HTTP | Price feed worker |
| Analyzer | 5011 | HTTP | Signal calculator |
| Strategy | 5012 | HTTP | Trading brain |
| RiskGuard | 5013 | gRPC | Order validation |
| OrderExecutor | 5014 | gRPC | Order execution |
| Notifier | 5015 | HTTP | Telegram alerts |
| PostgreSQL | 5433 | TCP | Database |
| Redis | 6379 | TCP | Message broker |

## 🔧 Environment Variables

Required in `.env`:
- `BINANCE_API_KEY` - Your Binance API key (testnet recommended)
- `BINANCE_API_SECRET` - Your Binance API secret
- `TELEGRAM_BOT_TOKEN` - Telegram bot token
- `TELEGRAM_CHAT_ID` - Your Telegram chat ID
- `POSTGRES_USER` - Database user (default: trader)
- `POSTGRES_PASSWORD` - Database password (default: strongpassword)

## 📈 Docker Compose Orchestration

### Service Dependencies Graph

```
        PostgreSQL (5433)
        Redis (6379)
        ↓
    ┌───┴────────────┬──────────┬──────────┐
    ↓                ↓          ↓          ↓
Ingestor        Analyzer   Executor   RiskGuard
(5010)          (5011)     (5014)     (5013)
    ↓                ↓          ↑          ↑
    └───────┬────────┴──────────┴──────────┘
            ↓
        Strategy (5012)
            ↓
    ┌───────┼────────┐
    ↓       ↓        ↓
Notifier Gateway  (All services)
(5015)   (5000)
```

### Startup Order (Automatic)

1. PostgreSQL & Redis start (dependencies)
2. DataIngestor starts
3. Analyzer starts
4. RiskGuard & OrderExecutor start (no inter-dependencies)
5. Strategy starts (waits for RiskGuard & Executor)
6. Notifier starts
7. Gateway starts (waits for all services)

## 📝 Key Files

```
infrastructure/
├── docker-compose.yml      ← All service configuration
├── .env                    ← Your credentials (DON'T COMMIT!)
├── .env.template           ← Template for .env
├── init.sql                ← Database schema
└── README.md               ← Detailed documentation

Dockerfiles:
├── src/Services/DataIngestor/Ingestor.Worker/Dockerfile
├── src/Services/Analyzer/Analyzer.Worker/Dockerfile
├── src/Services/Strategy/Strategy.Worker/Dockerfile
├── src/Services/RiskGuard/RiskGuard.API/Dockerfile
├── src/Services/Executor/Executor.API/Dockerfile
├── src/Services/Notifier/Notifier.Worker/Dockerfile
└── src/Gateway/Gateway.API/Dockerfile
```

## 🧪 Testing After Startup

### Verify Redis

```powershell
docker compose exec redis redis-cli ping
# Response: PONG
```

### Verify PostgreSQL

```powershell
docker compose exec postgres psql -U trader -d cryptotrading -c "SELECT 1"
# Response: (1 row)
```

### Verify Services Starting

```powershell
# Check logs
docker compose logs ingestor --tail=5

# Expected: "Ingestor service starting..."
```

## 🐛 Troubleshooting

### Service won't start?
```powershell
docker compose logs service_name --tail=20
# Check the error message
```

### Port already in use?
```powershell
# Change port in docker-compose.yml
# Before: "5010:5010"
# After:  "5020:5010"
docker compose up -d
```

### Need to rebuild after code changes?
```powershell
docker compose build service_name
docker compose up -d service_name
```

### Database lost on restart?
```powershell
# Data is persisted in postgres_data volume
# Only lost if you run: docker compose down -v
```

## 📚 Full Documentation

See:
- `DEPLOYMENT.md` - Complete deployment guide
- `infrastructure/README.md` - Infrastructure details
- `README.md` - Project overview

## ✨ Next Steps

1. Create `.env` file with your credentials
2. Run `docker compose build`
3. Run `docker compose up -d`
4. Monitor: `docker compose logs -f`
5. Access Gateway at http://localhost:5000
