using HistoricalCollector.Worker.Configuration;
using HistoricalCollector.Worker.Infrastructure;
using Microsoft.Extensions.Options;

namespace HistoricalCollector.Worker.Workers;

public sealed class GapFillingWorker : BackgroundService
{
    private readonly HistoricalDataSettings _historicalSettings;
    private readonly GapFillingSettings _gapSettings;
    private readonly PriceTickBatchRepository _repository;
    private readonly HistoricalIngestionService _ingestionService;
    private readonly ILogger<GapFillingWorker> _logger;

    public GapFillingWorker(
        IOptions<HistoricalDataSettings> historicalSettings,
        IOptions<GapFillingSettings> gapSettings,
        PriceTickBatchRepository repository,
        HistoricalIngestionService ingestionService,
        ILogger<GapFillingWorker> logger)
    {
        _historicalSettings = historicalSettings.Value;
        _gapSettings = gapSettings.Value;
        _repository = repository;
        _ingestionService = ingestionService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_historicalSettings.Enabled)
        {
            _logger.LogInformation("Gap filling worker skipped because HistoricalData:Enabled=true (one-time backfill mode)");
            return;
        }

        if (!_gapSettings.Enabled)
        {
            _logger.LogInformation("Gap filling is disabled. Set GapFilling:Enabled=true to run.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRunUtc();
            _logger.LogInformation("Next gap scan in {Delay}", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunGapCycleAsync(stoppingToken);
        }
    }

    private async Task RunGapCycleAsync(CancellationToken cancellationToken)
    {
        if (_historicalSettings.Symbols.Count == 0)
        {
            _logger.LogWarning("Gap cycle skipped because no symbols are configured");
            return;
        }

        var startUtc = _historicalSettings.StartDate.Date;
        var endUtc = DateTime.UtcNow.Date;

        var created = await _repository.DetectAndStoreDailyGapsAsync(
            _historicalSettings.Symbols,
            _historicalSettings.Interval,
            startUtc,
            endUtc,
            _gapSettings.ExpectedCandlesPerDay,
            cancellationToken);

        _logger.LogInformation("Gap detection completed. New gaps inserted: {Count}", created);

        var openGaps = await _repository.GetOpenGapsAsync(_historicalSettings.Interval, maxRows: 500, cancellationToken);
        _logger.LogInformation("Open gaps to fill: {Count}", openGaps.Count);

        foreach (var gap in openGaps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var inserted = await _ingestionService.IngestRangeAsync(
                    gap.Symbol,
                    gap.GapStart,
                    gap.GapEnd,
                    cancellationToken);

                await _repository.MarkGapFilledAsync(gap.Id, cancellationToken);
                _logger.LogInformation(
                    "Gap filled for {Symbol} {Start} -> {End}; inserted={Inserted}",
                    gap.Symbol,
                    gap.GapStart,
                    gap.GapEnd,
                    inserted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fill gap id={GapId} for {Symbol}", gap.Id, gap.Symbol);
            }
        }
    }

    private TimeSpan GetDelayUntilNextRunUtc()
    {
        if (!TimeSpan.TryParse(_gapSettings.DailyCheckUtc, out var runAt))
        {
            runAt = new TimeSpan(2, 0, 0);
        }

        var now = DateTime.UtcNow;
        var next = now.Date.Add(runAt);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        return next - now;
    }
}
