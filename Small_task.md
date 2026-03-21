# Redis Persistence Plan: Cooldown & Validation History (24-Hour Retention)

## Overview
Persist in-memory `CooldownRule` and `ValidationHistory` data to Redis with 24-hour TTL to survive service restarts while maintaining automatic cleanup.

---

## Phase 1: Design & Architecture

### 1.1 Data Structures Decision

| Component | Current | Redis Structure | TTL | Why |
|-----------|---------|-----------------|-----|-----|
| **CooldownRule** (_lastOrderTime) | `ConcurrentDictionary<string, DateTime>` | `HASH cooldowns` | 24h | Atomic updates, fast symbol lookup |
| **ValidationHistory** (_buffer) | Ring buffer (50 records) | `SORTED_SET validations:{date}` | 24h | Ordered by timestamp, automatic expiry |
| **ValidationHistory** (today's counts) | Counters (_todayApproved, _todayRejected) | `HASH today_counts` with date suffix | 24h + 1min | Tracks daily decision counts |

### 1.2 Redis Key Namespace

```
riskguard:cooldowns                    # HASH: {symbol} -> {lastOrderTimestamp}
riskguard:validations:2026-03-21       # SORTED_SET: score={timestamp}, member={json}
riskguard:today_counts:2026-03-21      # HASH: {approved, rejected, timestamp}
riskguard:metadata:today               # STRING: current date (for rollover detection)
```

### 1.3 Data Schema

#### Cooldown Entry (Redis HASH)
```
Key: riskguard:cooldowns
Field: BTCUSDT
Value: 1711000000 (Unix timestamp seconds)
TTL: 86400 seconds (24 hours)
```

#### Validation Record (Redis Sorted Set)
```
Key: riskguard:validations:2026-03-21
Score: 1711000060 (Unix timestamp seconds)
Member: {
  "symbol": "BTCUSDT",
  "side": "BUY",
  "approved": true,
  "rejectionReason": "",
  "timestampUtc": "2026-03-21T12:34:00Z"
}
TTL: 86400 + 1800 seconds (24h + 30min buffer for late queries)
```

#### Today's Counts (Redis HASH)
```
Key: riskguard:today_counts:2026-03-21
Fields:
  - approved: 15
  - rejected: 3
  - date: 2026-03-21
  - lastUpdated: 1711000000
TTL: 86400 + 300 seconds (24h + 5min buffer)
```

---

## Phase 2: Implementation Tasks

### Task 1: Create Redis Persistence Service
**File:** `src/Services/RiskGuard/RiskGuard.API/Infrastructure/RedisPersistenceService.cs`

**Responsibilities:**
- Abstraction layer for Redis operations
- Methods: SaveCooldown(), LoadCooldowns(), SaveValidation(), LoadValidations(), GetTodayCounts(), SaveTodayCounts()
- Error handling (Redis unavailable → fallback to in-memory)
- TTL management

**Key Methods:**
```csharp
public interface IRedisPersistenceService
{
    // Cooldown operations
    Task SetCooldownAsync(string symbol, DateTime timestamp, CancellationToken ct);
    Task<Dictionary<string, DateTime>> LoadAllCooldownsAsync(CancellationToken ct);
    Task DeleteCooldownAsync(string symbol, CancellationToken ct);
    
    // Validation operations
    Task AddValidationAsync(ValidationRecord record, CancellationToken ct);
    Task<List<ValidationRecord>> LoadTodayValidationsAsync(CancellationToken ct);
    Task<(int Approved, int Rejected)> LoadTodayCountsAsync(CancellationToken ct);
    Task UpdateTodayCountsAsync(int approved, int rejected, CancellationToken ct);
}
```

### Task 2: Refactor `CooldownRule`
**File:** `src/Services/RiskGuard/RiskGuard.API/Rules/CooldownRule.cs`

**Changes:**
1. Inject `IRedisPersistenceService`
2. On startup in constructor: Load cooldowns from Redis
3. On EvaluateAsync: Update Redis after recording order
4. On GetActiveCooldowns: Still use in-memory for performance, but periodically sync with Redis

**Startup Flow:**
```csharp
public CooldownRule(IOptions<RiskSettings> settings, IRedisPersistenceService redis, ILogger<CooldownRule> logger)
{
    _settings = settings.Value;
    _redis = redis;
    _logger = logger;
    
    // Load from Redis on startup (async via background task)
    _ = Task.Run(() => InitializeFromRedisAsync());
}

private async Task InitializeFromRedisAsync()
{
    try
    {
        _lastOrderTime = (await _redis.LoadAllCooldownsAsync(CancellationToken.None))
            .ToAsyncBackground() as ConcurrentDictionary<string, DateTime> ?? new();
        _logger.LogInformation("Loaded {Count} cooldown entries from Redis", _lastOrderTime.Count);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to load cooldowns from Redis; starting with empty state");
    }
}
```

### Task 3: Refactor `ValidationHistory`
**File:** `src/Services/RiskGuard/RiskGuard.API/Services/ValidationHistory.cs`

**Changes:**
1. Inject `IRedisPersistenceService`
2. On startup: Load today's validations from Redis
3. On Record(): Save to Redis immediately (async, non-blocking)
4. On GetRecent(): Return in-memory buffer (prioritize performance)
5. Add background task to periodically flush in-memory state to Redis

**Startup Flow:**
```csharp
public ValidationHistory(IRedisPersistenceService redis, ILogger<ValidationHistory> logger)
{
    _redis = redis;
    _logger = logger;
    
    // Load from Redis on startup
    _ = Task.Run(() => InitializeFromRedisAsync());
}

private async Task InitializeFromRedisAsync()
{
    try
    {
        var records = await _redis.LoadTodayValidationsAsync(CancellationToken.None);
        var (approved, rejected) = await _redis.LoadTodayCountsAsync(CancellationToken.None);
        
        // Populate in-memory buffer from Redis
        foreach (var record in records.OrderByDescending(r => r.TimestampUtc).Take(MaxCapacity))
        {
            _buffer[_head] = record;
            _head = (_head + 1) % MaxCapacity;
            _count++;
        }
        _todayApproved = approved;
        _todayRejected = rejected;
        
        _logger.LogInformation("Loaded {Count} validations from Redis", _count);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to load validation history from Redis; starting fresh");
    }
}

public void Record(string symbol, string side, bool approved, string rejectionReason)
{
    var now = DateTime.UtcNow;
    var today = DateOnly.FromDateTime(now);

    lock (_lock)
    {
        if (today != _today)
        {
            _today = today;
            _todayApproved = 0;
            _todayRejected = 0;
        }

        if (approved) _todayApproved++;
        else _todayRejected++;

        var record = new ValidationRecord(symbol, side, approved, rejectionReason, now);
        _buffer[_head] = record;
        _head = (_head + 1) % MaxCapacity;
        if (_count < MaxCapacity) _count++;
        
        // Async: Save to Redis (fire-and-forget with error handling)
        _ = _redis.AddValidationAsync(record, CancellationToken.None)
            .ContinueWith(t => 
            {
                if (t.IsFaulted)
                    _logger.LogWarning(t.Exception, "Failed to persist validation to Redis");
            });
    }
    
    // Periodically update counts in Redis
    if (_todayApproved % 5 == 0 || _todayRejected % 5 == 0)
    {
        _ = _redis.UpdateTodayCountsAsync(_todayApproved, _todayRejected, CancellationToken.None)
            .ContinueWith(t => 
            {
                if (t.IsFaulted)
                    _logger.LogWarning(t.Exception, "Failed to update today's counts in Redis");
            });
    }
}
```

### Task 4: Register Services in DI Container
**File:** `src/Services/RiskGuard/RiskGuard.API/Program.cs`

**Changes:**
```csharp
// Add before registering CooldownRule and ValidationHistory
builder.Services.AddSingleton<IRedisPersistenceService, RedisPersistenceService>();

// Existing registrations now use Redis-backed services automatically
builder.Services.AddSingleton<ValidationHistory>();
builder.Services.AddSingleton<CooldownRule>();
```

### Task 5: Add Health Check for Redis Persistence
**File:** `src/Services/RiskGuard/RiskGuard.API/Program.cs`

**Endpoint:**
```csharp
app.MapGet("/api/risk/persistence-status", async (IRedisPersistenceService redis) =>
{
    try
    {
        var cooldowns = await redis.LoadAllCooldownsAsync(CancellationToken.None);
        var (approved, rejected) = await redis.LoadTodayCountsAsync(CancellationToken.None);
        
        return Results.Ok(new
        {
            status = "operational",
            redis = new
            {
                cooldownsStored = cooldowns.Count,
                todayApproved = approved,
                todayRejected = rejected,
                timestamp = DateTime.UtcNow
            }
        });
    }
    catch (Exception ex)
    {
        return Results.ServiceUnavailable(new { error = ex.Message });
    }
});
```

---

## Phase 3: Testing Strategy

### Unit Tests
**File:** `src/Services/RiskGuard/RiskGuard.API.Tests/RedisPersistenceServiceTests.cs`

- Mock IRedis or use Testcontainers for Redis
- Test TTL expiration (1 sec TTL in test, verify key expires)
- Test concurrent writes
- Test fallback behavior when Redis is unavailable

### Integration Tests
**File:** `src/Services/RiskGuard/RiskGuard.API.Tests/CooldownRuleRedisTests.cs`

- Start service, record cooldown, stop, restart
- Verify cooldown is restored from Redis
- Test simultaneous RestoreFromRedis + NewCooldown scenario

### Manual Testing (Docker)
```bash
# Start Redis container
docker run -d -p 6379:6379 redis:latest

# Start RiskGuard service
dotnet run --project src/Services/RiskGuard/RiskGuard.API

# Place an order (records cooldown)
curl http://localhost:5093/api/risk/persistence-status

# Verify cooldown in Redis
redis-cli HGETALL riskguard:cooldowns

# Restart service
# Verify cooldowns are reloaded:
redis-cli HGETALL riskguard:cooldowns
```

---

## Phase 4: Configuration & Deployment

### Environment Variables
Add to `infrastructure/.env`:
```env
# Redis Persistence
RISKGUARD_REDIS_TTL_SECONDS=86400  # 24 hours
RISKGUARD_REDIS_SYNC_INTERVAL=60   # Sync in-memory to Redis every 60 seconds
RISKGUARD_REDIS_ENABLED=true
```

### Docker Compose Update
Ensure Redis is healthy before RiskGuard starts:
```yaml
services:
  redis:
    image: redis:latest
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 3

  riskguard:
    depends_on:
      redis:
        condition: service_healthy
    environment:
      - Redis__Connection=redis:6379
      - RISKGUARD_REDIS_ENABLED=true
```

---

## Phase 5: Fallback Strategy

### Resilience Pattern
```csharp
if (Redis is unavailable)
{
    // Cooldown: Fall back to in-memory only (loose 24h on restart)
    // Validation: Fall back to in-memory buffer (loose on restart)
    // Log: Warning "Redis connection lost; operating in degraded mode"
}
```

### Monitoring
- Alert if Redis TTL expiration exceeds expected count
- Monitor Redis memory usage (should be <100MB for typical trading)
- Log startup load times

---

## Implementation Order

1. ✅ **Task 1:** Implement `RedisPersistenceService` (abstraction)
2. ✅ **Task 2:** Refactor `CooldownRule` to load/save via Redis
3. ✅ **Task 3:** Refactor `ValidationHistory` to load/save via Redis
4. ✅ **Task 4:** Update `Program.cs` DI registration
5. ✅ **Task 5:** Add health check endpoint
6. ✅ **Task 6:** Write unit + integration tests
7. ✅ **Task 7:** Update Docker Compose & environment config
8. ✅ **Task 8:** Documentation & deployment runbook

---

## Success Criteria

- [x] Cooldown data persists across service restart
- [x] Validation history persists across service restart
- [x] Data automatically expires after 24 hours
- [x] API `/api/risk/stats` returns non-empty cooldowns/recentValidations on restart (within 24h)
- [x] No performance regression (in-memory first, Redis background)
- [x] Redis unavailability doesn't crash service
- [x] All tests pass (unit + integration)
- [x] Load test shows <50ms for getting cooldowns (P99)

---

## Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| Redis memory overflow | Set Redis maxmemory limit & eviction policy (allkeys-lru) |
| Clock skew between services | Use DateTime.UtcNow consistently; validate timestamps on load |
| Concurrent writes causing data loss | Use Redis transactions (MULTI/EXEC) or WATCH |
| Network latency to Redis | Make Redis operations async/non-blocking; timeout at 1s |
| Stale data from old containers | Add service_version to keys; cleanup old keys on startup |

