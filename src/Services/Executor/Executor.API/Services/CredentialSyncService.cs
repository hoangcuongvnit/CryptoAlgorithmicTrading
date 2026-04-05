using Executor.API.Configuration;
using Executor.API.Infrastructure;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Net.Sockets;

namespace Executor.API.Services;

/// <summary>
/// Runs once on startup. Pulls Binance credentials from Gateway's internal endpoint
/// and reconfigures BinanceRestClientProvider. Falls back to env var / appsettings keys
/// if Gateway is unreachable or has no credentials stored.
/// Always signals CredentialSyncGate when done so StartupReconciliationService can proceed.
/// </summary>
public sealed class CredentialSyncService : BackgroundService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly BinanceRestClientProvider _clientProvider;
    private readonly IOptions<BinanceSettings> _binanceOpts;
    private readonly CredentialSyncGate _gate;
    private readonly ILogger<CredentialSyncService> _logger;

    public CredentialSyncService(
        IHttpClientFactory httpFactory,
        BinanceRestClientProvider clientProvider,
        IOptions<BinanceSettings> binanceOpts,
        CredentialSyncGate gate,
        ILogger<CredentialSyncService> logger)
    {
        _httpFactory    = httpFactory;
        _clientProvider = clientProvider;
        _binanceOpts    = binanceOpts;
        _gate           = gate;
        _logger         = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SyncCredentialsAsync(stoppingToken);
        }
        finally
        {
            // Always unblock StartupReconciliationService, even on unexpected failure.
            _gate.Complete();
        }
    }

    private async Task SyncCredentialsAsync(CancellationToken ct)
    {
        _logger.LogInformation("CredentialSyncService: pulling credentials from Gateway.");
        const int maxAttempts = 3;
        const int delayMs = 2000;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var client = _httpFactory.CreateClient("gateway");
                using var response = await client.GetAsync("/api/internal/exchange/credentials", ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "CredentialSyncService: Gateway returned {StatusCode}. Using env var credentials.",
                        (int)response.StatusCode);
                    return;
                }

                var payload = await response.Content
                    .ReadFromJsonAsync<CredentialsPayload>(cancellationToken: ct);

                if (payload is null || !payload.Configured)
                {
                    _logger.LogWarning(
                        "CredentialSyncService: Gateway has no credentials stored. Using env var credentials.");
                    return;
                }

                var settings = _binanceOpts.Value;
                settings.ApiKey           = payload.ApiKey;
                settings.ApiSecret        = payload.ApiSecret;
                settings.TestnetApiKey    = payload.TestnetApiKey;
                settings.TestnetApiSecret = payload.TestnetApiSecret;
                settings.UseTestnet       = payload.UseTestnet;

                var activeKey    = payload.UseTestnet && !string.IsNullOrEmpty(payload.TestnetApiKey)
                                   ? payload.TestnetApiKey    : payload.ApiKey;
                var activeSecret = payload.UseTestnet && !string.IsNullOrEmpty(payload.TestnetApiSecret)
                                   ? payload.TestnetApiSecret : payload.ApiSecret;

                _clientProvider.Reconfigure(activeKey, activeSecret, payload.UseTestnet);

                _logger.LogInformation(
                    "CredentialSyncService: credentials applied from Gateway. UseTestnet={UseTestnet}",
                    payload.UseTestnet);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("CredentialSyncService: sync cancelled during shutdown.");
                return;
            }
            catch (TaskCanceledException)
            {
                if (attempt == maxAttempts)
                {
                    _logger.LogWarning(
                        "CredentialSyncService: timed out while contacting Gateway after {Attempts} attempts. Using env var credentials.",
                        maxAttempts);
                    return;
                }

                _logger.LogInformation(
                    "CredentialSyncService: Gateway timeout (attempt {Attempt}/{Max}). Retrying in {DelayMs}ms...",
                    attempt, maxAttempts, delayMs);
            }
            catch (HttpRequestException ex) when (ex.InnerException is SocketException se)
            {
                if (attempt == maxAttempts)
                {
                    _logger.LogWarning(
                        "CredentialSyncService: Gateway host resolution/connect failed ({SocketError}) after {Attempts} attempts. Using env var credentials.",
                        se.SocketErrorCode, maxAttempts);
                    return;
                }

                _logger.LogInformation(
                    "CredentialSyncService: Gateway DNS/network not ready ({SocketError}) (attempt {Attempt}/{Max}). Retrying in {DelayMs}ms...",
                    se.SocketErrorCode, attempt, maxAttempts, delayMs);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == maxAttempts)
                {
                    _logger.LogWarning(
                        "CredentialSyncService: could not reach Gateway after {Attempts} attempts ({Message}). Using env var credentials.",
                        maxAttempts, ex.Message);
                    return;
                }

                _logger.LogInformation(
                    "CredentialSyncService: Gateway request failed (attempt {Attempt}/{Max}). Retrying in {DelayMs}ms...",
                    attempt, maxAttempts, delayMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "CredentialSyncService: unexpected error while syncing credentials. Using env var credentials.");
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
