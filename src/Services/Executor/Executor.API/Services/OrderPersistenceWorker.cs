using CryptoTrading.Shared.DTOs;
using Executor.API.Infrastructure;

namespace Executor.API.Services;

public sealed class OrderPersistenceWorker : BackgroundService
{
    private readonly OrderWriteQueue _queue;
    private readonly OrderRepository _repository;
    private readonly SystemEventPublisher _systemEvents;
    private readonly ILogger<OrderPersistenceWorker> _logger;

    public OrderPersistenceWorker(
        OrderWriteQueue queue,
        OrderRepository repository,
        SystemEventPublisher systemEvents,
        ILogger<OrderPersistenceWorker> logger)
    {
        _queue = queue;
        _repository = repository;
        _systemEvents = systemEvents;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrderPersistenceWorker started");

        await foreach (var (request, result) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _repository.PersistAsync(request, result, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order persistence failed for {Symbol} order {OrderId}",
                    request.Symbol, result.OrderId);

                _ = _systemEvents.PublishAsync(new SystemEvent
                {
                    Type = SystemEventType.Error,
                    ServiceName = "Executor",
                    Message = $"DB persistence failed for {request.Symbol} order {result.OrderId}: {ex.Message}",
                    Timestamp = DateTime.UtcNow,
                    ErrorCode = TradingErrorCode.DbPersistenceFailed,
                    Symbol = request.Symbol
                }, CancellationToken.None);
            }
        }

        _logger.LogInformation("OrderPersistenceWorker stopped");
    }
}
