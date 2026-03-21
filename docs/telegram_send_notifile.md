# Telegram Notification Batching System - Implementation Plan

## Problem Statement

Currently, every trade execution/transaction generates an immediate Telegram notification, resulting in:
- **Too many messages** sent to Telegram chat (notification spam)
- **Overwhelmed user** unable to review important alerts quickly
- **Potential rate limiting** from Telegram API
- **Poor UX** where critical alerts get lost among routine notifications

## Solution Overview

Implement a **notification batching system** that aggregates non-critical informational notifications every **5 minutes** and sends them as a single consolidated message, while preserving immediate delivery for critical/approval-required notifications.

---

## High-Level Architecture

### Notification Categories

1. **Critical Notifications** (Immediate Delivery)
   - Risk Guard rejections / rule violations
   - Order execution errors
   - System alerts / errors
   - **Delivery**: Send immediately

2. **Informational Notifications** (Batched Delivery)
   - Trade signals generated
   - Orders submitted successfully
   - Position updates
   - Price updates / technical indicator alerts
   - **Delivery**: Batch every 5 minutes

### Component Design

```
Transaction/Event Triggered
           ↓
    ┌──────────────────┐
    │ Classify Notice  │
    └──────┬───────────┘
           ↓
    ┌──────────────────────────┐
    │ Critical? ├─→ Send Now   │
    └─────┬────────────────────┘
          │ No
          ↓
    ┌──────────────────────────────┐
    │ Add to Batch Queue           │
    │ (indexed by notification ID) │
    └─────┬────────────────────────┘
          ↓
    ┌─────────────────────────────────────┐
    │ Batch Timer (5 min interval)        │
    │ - Aggregate pending messages        │
    │ - Format into single message        │
    │ - Send consolidated notification   │
    │ - Clear queue                       │
    └─────────────────────────────────────┘
```

---

## Implementation Strategy

### Phase 1: Create Notification Service Layer

**File**: `src/Shared/Services/NotificationBatcher.cs`

```csharp
public interface INotificationBatcher
{
    Task SendCriticalAsync(string message);
    Task EnqueueBatchedAsync(NotificationItem item);
    Task StartBatchProcessing(TimeSpan interval);
}

public class NotificationBatcher : INotificationBatcher
{
    private Queue<NotificationItem> _batchQueue = new();
    private Timer _batchTimer;
    private readonly ILogger<NotificationBatcher> _logger;
    
    public async Task SendCriticalAsync(string message)
    {
        // Send immediately to Telegram
        await _telegramService.SendMessageAsync(message);
    }
    
    public async Task EnqueueBatchedAsync(NotificationItem item)
    {
        lock (_batchQueue)
        {
            _batchQueue.Enqueue(item);
        }
    }
    
    public void StartBatchProcessing(TimeSpan interval)
    {
        // Start 5-minute interval timer
        _batchTimer = new Timer(ProcessBatch, null, interval, interval);
    }
    
    private async void ProcessBatch(object state)
    {
        NotificationItem[] itemsToSend;
        
        lock (_batchQueue)
        {
            if (_batchQueue.Count == 0) return;
            
            itemsToSend = _batchQueue.ToArray();
            _batchQueue.Clear();
        }
        
        string consolidatedMessage = FormatBatchMessage(itemsToSend);
        await _telegramService.SendMessageAsync(consolidatedMessage);
    }
    
    private string FormatBatchMessage(NotificationItem[] items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("📊 **Trading Activity Report (Last 5 Minutes)**");
        sb.AppendLine();
        
        var grouped = items.GroupBy(x => x.Category);
        foreach (var group in grouped)
        {
            sb.AppendLine($"**{group.Key}:**");
            foreach (var item in group)
            {
                sb.AppendLine($"  • {item.Message}");
            }
            sb.AppendLine();
        }
        
        sb.AppendLine($"Total: {items.Length} notifications");
        return sb.ToString();
    }
}
```

### Phase 2: Define Notification Categories

**File**: `src/Shared/Models/NotificationItem.cs`

```csharp
public enum NotificationCategory
{
    TradeSignal,
    OrderSubmitted,
    OrderFilled,
    PositionUpdate,
    PriceAlert,
    TechnicalIndicator,
    SystemInfo
}

public enum NotificationPriority
{
    Critical,   // Immediate send
    High,       // Batch but prioritize
    Normal      // Batch normally
}

public class NotificationItem
{
    public string Id { get; set; }
    public NotificationCategory Category { get; set; }
    public NotificationPriority Priority { get; set; }
    public string Message { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}
```

### Phase 3: Integrate with Notifier Service

**File**: `src/Services/Notifier/Notifier.Worker/NotifierService.cs`

**Changes Required:**

```csharp
public class NotifierService
{
    private readonly INotificationBatcher _batcher;
    
    public async Task SendNotificationAsync(NotificationItem notification)
    {
        if (notification.Priority == NotificationPriority.Critical)
        {
            await _batcher.SendCriticalAsync(notification.Message);
        }
        else
        {
            await _batcher.EnqueueBatchedAsync(notification);
        }
    }
}
```

### Phase 4: Update Signal Processing

All services that generate notifications must classify them:

**Strategy.Worker** → TradeSignal → `Priority.Normal`
**Executor.API** → Order rejection → `Priority.Critical`
**Analyzer.Worker** → Price alert → `Priority.Normal`
**RiskGuard.API** → Risk violation → `Priority.Critical`

---

## Configuration

### Add to `appsettings.json`

```json
{
  "Telegram": {
    "BotToken": "${TELEGRAM_BOT_TOKEN}",
    "ChatId": "${TELEGRAM_CHAT_ID}",
    "BatchInterval": "00:05:00",
    "EnableBatching": true,
    "MaxBatchSize": 100
  }
}
```

### Environment Variables

```bash
TELEGRAM_BATCH_INTERVAL=300          # 5 minutes in seconds
TELEGRAM_BATCH_ENABLED=true
TELEGRAM_BATCH_MAX_SIZE=100
```

---

## Batch Message Format Example

```
📊 **Trading Activity Report (Last 5 Minutes)**

**Trade Signal:**
  • BTCUSDT: Buy signal from RSI strategy @ 42,500 USDT
  • ETHUSDT: Sell signal from MACD strategy @ 2,250 USDT

**Order Submitted:**
  • Order #12345 submitted: BUY 0.5 BTC @ 42,480 USDT
  • Order #12346 submitted: SELL 5 ETH @ 2,255 USDT

**Order Filled:**
  • Order #12340 FILLED: 0.5 BTC @ 42,500 USDT (Profit: +$100)

**Position Update:**
  • Portfolio value updated: +$500 (2.1% gain)

**Technical Indicator:**
  • RSI oversold condition on 4H chart for BTCUSDT
  • MACD crossover detected on daily chart for ETHUSDT

Total: 7 notifications
```

---

## Implementation Steps

### Step 1: Create Core Batcher Service
- [ ] Create `NotificationBatcher.cs` with queue management
- [ ] Implement 5-minute timer logic
- [ ] Add thread-safe queue synchronization

### Step 2: Define Models
- [ ] Create `NotificationItem` class
- [ ] Define `NotificationCategory` enum
- [ ] Define `NotificationPriority` enum

### Step 3: Register in DI
- [ ] Add to `src/Services/Notifier/Notifier.Worker/Program.cs`
- [ ] Register `INotificationBatcher` service
- [ ] Start batch processing on application startup

### Step 4: Update All Notification Sources
- [ ] Strategy.Worker → Classify trade signals as `Priority.Normal`
- [ ] Executor.API → Classify order execution as `Priority.Normal`, rejections as `Priority.Critical`
- [ ] Analyzer.Worker → Classify price alerts as `Priority.Normal`
- [ ] RiskGuard.API → Classify rule violations as `Priority.Critical`

### Step 5: Update Notifier.Worker
- [ ] Modify notification sending logic to use batcher
- [ ] Call `SendCriticalAsync()` for critical notifications
- [ ] Call `EnqueueBatchedAsync()` for informational notifications

### Step 6: Configuration
- [ ] Add batch settings to `appsettings.json`
- [ ] Add environment variables to `.env.template`
- [ ] Update Docker environment configuration

### Step 7: Testing
- [ ] Unit tests for batcher queue logic
- [ ] Unit tests for message formatting
- [ ] Integration test with mock Telegram service
- [ ] Load test with high transaction volume

### Step 8: Monitoring
- [ ] Add metrics: batch size, send frequency, queue depth
- [ ] Add logs for batch processing events
- [ ] Monitor Telegram API response times

---

## Expected Outcomes

| Metric | Before | After |
|--------|--------|-------|
| Messages/hour (100 trades) | ~100 | ~12 (12 batches) |
| User attention required | High (spam) | Low (consolidated) |
| Critical alerts latency | Immediate | Immediate |
| Info alerts latency | Immediate | 0-5 minutes |
| Telegram API calls/hour | ~100 | ~12 |

---

## Failed Scenarios & Mitigation

| Scenario | Impact | Mitigation |
|----------|--------|-----------|
| Timer skips (process crash) | Batch lost | PersistQueue to Redis before sending |
| Telegram API rate limit | Message rejected | Implement exponential backoff + retry |
| Message too large | Send fails | Split into multiple messages if >4K chars |
| Queue memory overload | High memory/crash | Implement max queue size with overflow logs |

---

## Future Enhancements

1. **Notification Preferences**: Per-user batch intervals (3min, 5min, 10min, 30min)
2. **Smart Scheduling**: Batch less frequently during low-activity periods
3. **Priority Escalation**: Escalate high-priority items in batch message
4. **Statistics**: Include daily/weekly trading stats in batch summaries
5. **Multi-destination**: Support Telegram, Email, Discord with same batching logic
6. **User Dashboard**: Web interface to view pending/sent notifications

---

## References

- Telegram Bot API: https://core.telegram.org/bots/api
- Message size limits: 4096 characters per message
- Rate limits: ~100 messages per second per account (soft limit)

