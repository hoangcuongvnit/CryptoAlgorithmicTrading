using System.IO.Compression;

namespace HistoricalCollector.Worker.Infrastructure;

public sealed class BinanceVisionClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BinanceVisionClient> _logger;

    public BinanceVisionClient(HttpClient httpClient, ILogger<BinanceVisionClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string?> DownloadMonthlyKlinesAsync(
        string baseUrl,
        string symbol,
        string interval,
        DateTime month,
        string downloadPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(downloadPath);

        var yyyyMm = month.ToString("yyyy-MM");
        var fileName = $"{symbol}-{interval}-{yyyyMm}.zip";
        var zipPath = Path.Combine(downloadPath, fileName);

        var url = $"{baseUrl.TrimEnd('/')}/data/spot/monthly/klines/{symbol}/{interval}/{fileName}";

        if (!File.Exists(zipPath))
        {
            _logger.LogInformation("Downloading {Url}", url);

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("File not found or unavailable: {Url} ({Status})", url, response.StatusCode);
                return null;
            }

            await using var fs = File.Create(zipPath);
            await response.Content.CopyToAsync(fs, cancellationToken);
        }

        var extractDir = Path.Combine(downloadPath, $"{symbol}-{interval}-{yyyyMm}");
        Directory.CreateDirectory(extractDir);

        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
        var csvPath = Directory.GetFiles(extractDir, "*.csv", SearchOption.TopDirectoryOnly).FirstOrDefault();
        return csvPath;
    }
}
