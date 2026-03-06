using System.Diagnostics;
using CryptoTrading.Shared.DTOs;
using Ingestor.Worker.Configuration;
using Ingestor.Worker.Infrastructure;
using Microsoft.Extensions.Options;

namespace Ingestor.Worker.Workers;

public sealed class PriceTickPersistenceWorker : BackgroundService
{
    private readonly PriceTickWriteQueue _queue;
    private readonly PriceTickRepository _repository;
    private readonly PersistenceSettings _settings;
    private readonly ILogger<PriceTickPersistenceWorker> _logger;

    public PriceTickPersistenceWorker(
        PriceTickWriteQueue queue,
        PriceTickRepository repository,
        IOptions<PersistenceSettings> settings,
        ILogger<PriceTickPersistenceWorker> logger)
    {
        _queue = queue;
        _repository = repository;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PriceTickPersistenceWorker started with BatchSize={BatchSize}, FlushInterval={FlushIntervalSeconds}s",
            _settings.BatchSize,
            _settings.FlushIntervalSeconds);

        var batch = new List<PriceTick>(_settings.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                flushCts.CancelAfter(TimeSpan.FromSeconds(_settings.FlushIntervalSeconds));

                while (batch.Count < _settings.BatchSize &&
                       await _queue.Reader.WaitToReadAsync(flushCts.Token))
                {
                    while (batch.Count < _settings.BatchSize && _queue.Reader.TryRead(out var tick))
                    {
                        batch.Add(tick);
                    }

                    if (batch.Count >= _settings.BatchSize)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
            }

            if (batch.Count == 0)
            {
                continue;
            }

            await PersistBatchAsync(batch, stoppingToken);
            batch.Clear();
        }

        while (_queue.Reader.TryRead(out var tick))
        {
            batch.Add(tick);
            if (batch.Count >= _settings.BatchSize)
            {
                await PersistBatchAsync(batch, stoppingToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await PersistBatchAsync(batch, stoppingToken);
        }

        _logger.LogInformation("PriceTickPersistenceWorker stopped");
    }

    private async Task PersistBatchAsync(List<PriceTick> batch, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var affected = await _repository.UpsertBatchAsync(batch, cancellationToken);
        sw.Stop();

        _logger.LogDebug(
            "Persisted batch: input={Input}, inserted={Inserted}, durationMs={DurationMs}",
            batch.Count,
            affected,
            sw.ElapsedMilliseconds);
    }
}
