using HistoricalCollector.Worker.Configuration;
using HistoricalCollector.Worker.Infrastructure;
using Microsoft.Extensions.Options;

namespace HistoricalCollector.Worker.Workers;

public sealed class HistoricalBackfillWorker : BackgroundService
{
    private readonly HistoricalDataSettings _settings;
    private readonly HistoricalIngestionService _ingestionService;
    private readonly ILogger<HistoricalBackfillWorker> _logger;

    public HistoricalBackfillWorker(
        IOptions<HistoricalDataSettings> settings,
        HistoricalIngestionService ingestionService,
        ILogger<HistoricalBackfillWorker> logger)
    {
        _settings = settings.Value;
        _ingestionService = ingestionService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Historical backfill is disabled. Set HistoricalData:Enabled=true to run.");
            return;
        }

        _logger.LogInformation(
            "Starting historical backfill: {StartDate} -> {EndDate}, symbols={Count}, interval={Interval}",
            _settings.StartDate,
            _settings.EndDate,
            _settings.Symbols.Count,
            _settings.Interval);

        foreach (var symbol in _settings.Symbols)
        {
            await BackfillSymbolAsync(symbol, stoppingToken);
        }

        _logger.LogInformation("Historical backfill completed");
    }

    private async Task BackfillSymbolAsync(string symbol, CancellationToken cancellationToken)
    {
        var inserted = await _ingestionService.IngestRangeAsync(
            symbol,
            _settings.StartDate,
            _settings.EndDate.AddDays(1).AddTicks(-1),
            cancellationToken);

        _logger.LogInformation("Backfill completed for {Symbol}: inserted {Count} rows", symbol, inserted);
    }
}
