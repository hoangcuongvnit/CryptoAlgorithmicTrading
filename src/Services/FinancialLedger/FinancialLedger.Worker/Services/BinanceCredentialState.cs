using FinancialLedger.Worker.Configuration;

namespace FinancialLedger.Worker.Services;

public sealed class BinanceCredentialState
{
    private readonly object _lock = new();
    private BinanceAccountSettings _settings;

    public BinanceCredentialState(LedgerSettings ledgerSettings)
    {
        _settings = Clone(ledgerSettings.Binance);
    }

    public BinanceAccountSettings GetSnapshot()
    {
        lock (_lock)
        {
            return Clone(_settings);
        }
    }

    public void ApplyFromGateway(
        string apiKey,
        string apiSecret,
        string testnetApiKey,
        string testnetApiSecret,
        bool useTestnet)
    {
        lock (_lock)
        {
            _settings.ApiKey = apiKey;
            _settings.ApiSecret = apiSecret;
            _settings.TestnetApiKey = testnetApiKey;
            _settings.TestnetApiSecret = testnetApiSecret;
            _settings.UseTestnet = useTestnet;
        }
    }

    private static BinanceAccountSettings Clone(BinanceAccountSettings source)
        => new()
        {
            ApiKey = source.ApiKey,
            ApiSecret = source.ApiSecret,
            TestnetApiKey = source.TestnetApiKey,
            TestnetApiSecret = source.TestnetApiSecret,
            UseTestnet = source.UseTestnet,
            RequestTimeoutSeconds = source.RequestTimeoutSeconds,
            MaxRetries = source.MaxRetries,
            RetryDelayMs = source.RetryDelayMs,
        };
}
