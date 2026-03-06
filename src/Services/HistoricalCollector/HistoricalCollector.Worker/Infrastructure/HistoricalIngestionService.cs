using CryptoTrading.Shared.DTOs;
using HistoricalCollector.Worker.Configuration;
using HistoricalCollector.Worker.Parsers;
using Microsoft.Extensions.Options;

namespace HistoricalCollector.Worker.Infrastructure;

public sealed class HistoricalIngestionService
{
    private readonly HistoricalDataSettings _settings;
    private readonly BinanceVisionClient _visionClient;
    private readonly PriceTickBatchRepository _repository;
    private readonly ILogger<HistoricalIngestionService> _logger;

    public HistoricalIngestionService(
        IOptions<HistoricalDataSettings> settings,
        BinanceVisionClient visionClient,
        PriceTickBatchRepository repository,
        ILogger<HistoricalIngestionService> logger)
    {
        _settings = settings.Value;
        _visionClient = visionClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task<int> IngestRangeAsync(
        string symbol,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var inserted = 0;
        var monthCursor = new DateTime(startUtc.Year, startUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endMonth = new DateTime(endUtc.Year, endUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        while (monthCursor <= endMonth && !cancellationToken.IsCancellationRequested)
        {
            var csvPath = await _visionClient.DownloadMonthlyKlinesAsync(
                _settings.BaseUrl,
                symbol,
                _settings.Interval,
                monthCursor,
                _settings.DownloadPath,
                cancellationToken);

            if (csvPath is not null)
            {
                inserted += await IngestCsvAsync(symbol, csvPath, startUtc, endUtc, cancellationToken);
            }

            monthCursor = monthCursor.AddMonths(1);
        }

        return inserted;
    }

    private async Task<int> IngestCsvAsync(
        string symbol,
        string csvPath,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var batch = new List<PriceTick>(_settings.BatchSize);
        var inserted = 0;

        await foreach (var line in ReadLinesAsync(csvPath, cancellationToken))
        {
            if (!KlineCsvParser.TryParseLine(line, symbol, _settings.Interval, out var tick) || tick is null)
            {
                continue;
            }

            if (tick.Timestamp < startUtc || tick.Timestamp > endUtc)
            {
                continue;
            }

            batch.Add(tick);
            if (batch.Count >= _settings.BatchSize)
            {
                inserted += await _repository.UpsertBatchAsync(batch, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            inserted += await _repository.UpsertBatchAsync(batch, cancellationToken);
        }

        _logger.LogInformation(
            "Imported {Inserted} rows for {Symbol} from {File} within range {Start} -> {End}",
            inserted,
            symbol,
            Path.GetFileName(csvPath),
            startUtc,
            endUtc);

        return inserted;
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(
        string filePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(filePath);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }
    }
}
