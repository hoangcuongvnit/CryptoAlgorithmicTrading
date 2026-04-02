using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Interfaces.Clients;
using CryptoExchange.Net.Authentication;
using Executor.API.Configuration;
using Microsoft.Extensions.Options;

namespace Executor.API.Infrastructure;

/// <summary>
/// Provides a <see cref="IBinanceRestClient"/> that can be reconfigured at runtime
/// to switch between Live and Testnet environments without restarting the service.
/// </summary>
public sealed class BinanceRestClientProvider : IDisposable
{
    private IBinanceRestClient _current;
    private readonly object _lock = new();

    public BinanceRestClientProvider(IOptions<BinanceSettings> settings)
        => _current = CreateClient(settings.Value);

    public IBinanceRestClient Current
    {
        get { lock (_lock) return _current; }
    }

    /// <summary>
    /// Recreates the underlying client with a new environment and credentials.
    /// Called by the runtime config reload endpoint when exchange settings change.
    /// </summary>
    public void Reconfigure(string apiKey, string apiSecret, bool useTestnet)
    {
        var settings = new BinanceSettings
        {
            ApiKey = apiKey,
            ApiSecret = apiSecret,
            UseTestnet = useTestnet
        };
        var newClient = CreateClient(settings);
        IBinanceRestClient old;
        lock (_lock)
        {
            old = _current;
            _current = newClient;
        }
        old.Dispose();
    }

    private static IBinanceRestClient CreateClient(BinanceSettings s)
        => new BinanceRestClient(opts =>
        {
            opts.Environment = s.UseTestnet
                ? BinanceEnvironment.Testnet
                : BinanceEnvironment.Live;
            if (!string.IsNullOrEmpty(s.ActiveApiKey))
                opts.ApiCredentials = new ApiCredentials(s.ActiveApiKey, s.ActiveApiSecret);
        });

    public void Dispose()
    {
        lock (_lock) _current?.Dispose();
    }
}
