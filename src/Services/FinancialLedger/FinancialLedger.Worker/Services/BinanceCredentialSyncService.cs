using System.Net.Http.Json;
using System.Net.Sockets;

namespace FinancialLedger.Worker.Services;

/// <summary>
/// Pulls Binance credentials from Gateway internal endpoint at startup.
/// Gateway is the source of truth because Settings UI stores credentials in DB.
/// </summary>
public sealed class BinanceCredentialSyncService : BackgroundService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly BinanceCredentialState _credentialState;
    private readonly ILogger<BinanceCredentialSyncService> _logger;

    public BinanceCredentialSyncService(
        IHttpClientFactory httpFactory,
        BinanceCredentialState credentialState,
        ILogger<BinanceCredentialSyncService> logger)
    {
        _httpFactory = httpFactory;
        _credentialState = credentialState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SyncCredentialsAsync(stoppingToken);
    }

    private async Task SyncCredentialsAsync(CancellationToken ct)
    {
        _logger.LogInformation("BinanceCredentialSyncService: pulling credentials from Gateway.");

        const int maxAttempts = 12;
        const int delayMs = 5000;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var client = _httpFactory.CreateClient("gateway");
                using var response = await client.GetAsync("/api/internal/exchange/credentials", ct);

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt == maxAttempts)
                    {
                        _logger.LogWarning(
                            "BinanceCredentialSyncService: Gateway returned {StatusCode} after {Attempts} attempts. Keeping local fallback credentials.",
                            (int)response.StatusCode,
                            maxAttempts);
                        return;
                    }

                    _logger.LogInformation(
                        "BinanceCredentialSyncService: Gateway returned {StatusCode} (attempt {Attempt}/{Max}). Retrying in {DelayMs}ms...",
                        (int)response.StatusCode,
                        attempt,
                        maxAttempts,
                        delayMs);
                }
                else
                {
                    var payload = await response.Content.ReadFromJsonAsync<CredentialsPayload>(cancellationToken: ct);
                    if (payload is null || !payload.Configured)
                    {
                        _logger.LogWarning(
                            "BinanceCredentialSyncService: Gateway has no credentials stored. Keeping local fallback credentials.");
                        return;
                    }

                    _credentialState.ApplyFromGateway(
                        payload.ApiKey,
                        payload.ApiSecret,
                        payload.TestnetApiKey,
                        payload.TestnetApiSecret,
                        payload.UseTestnet);

                    _logger.LogInformation(
                        "BinanceCredentialSyncService: credentials applied from Gateway. UseTestnet={UseTestnet}",
                        payload.UseTestnet);
                    return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("BinanceCredentialSyncService: sync cancelled during shutdown.");
                return;
            }
            catch (TaskCanceledException)
            {
                if (attempt == maxAttempts)
                {
                    _logger.LogWarning(
                        "BinanceCredentialSyncService: timed out while contacting Gateway after {Attempts} attempts. Keeping local fallback credentials.",
                        maxAttempts);
                    return;
                }

                _logger.LogInformation(
                    "BinanceCredentialSyncService: Gateway timeout (attempt {Attempt}/{Max}). Retrying in {DelayMs}ms...",
                    attempt,
                    maxAttempts,
                    delayMs);
            }
            catch (HttpRequestException ex) when (ex.InnerException is SocketException se)
            {
                if (attempt == maxAttempts)
                {
                    _logger.LogWarning(
                        "BinanceCredentialSyncService: Gateway DNS/network failed ({SocketError}) after {Attempts} attempts. Keeping local fallback credentials.",
                        se.SocketErrorCode,
                        maxAttempts);
                    return;
                }

                _logger.LogInformation(
                    "BinanceCredentialSyncService: Gateway DNS/network not ready ({SocketError}) (attempt {Attempt}/{Max}). Retrying in {DelayMs}ms...",
                    se.SocketErrorCode,
                    attempt,
                    maxAttempts,
                    delayMs);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == maxAttempts)
                {
                    _logger.LogWarning(
                        "BinanceCredentialSyncService: could not reach Gateway after {Attempts} attempts ({Message}). Keeping local fallback credentials.",
                        maxAttempts,
                        ex.Message);
                    return;
                }

                _logger.LogInformation(
                    "BinanceCredentialSyncService: Gateway request failed (attempt {Attempt}/{Max}). Retrying in {DelayMs}ms...",
                    attempt,
                    maxAttempts,
                    delayMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "BinanceCredentialSyncService: unexpected error while syncing credentials. Keeping local fallback credentials.");
                return;
            }

            await Task.Delay(delayMs, ct);
        }
    }

    private sealed record CredentialsPayload(
        bool Configured,
        string ApiKey,
        string ApiSecret,
        string TestnetApiKey,
        string TestnetApiSecret,
        bool UseTestnet);
}
