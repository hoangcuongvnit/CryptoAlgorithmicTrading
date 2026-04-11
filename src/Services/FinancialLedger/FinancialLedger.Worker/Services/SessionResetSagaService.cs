using FinancialLedger.Worker.Configuration;
using System.Text.Json;

namespace FinancialLedger.Worker.Services;

public sealed class SessionResetSagaService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly LedgerSettings _settings;
    private readonly ILogger<SessionResetSagaService> _logger;

    public SessionResetSagaService(
        IHttpClientFactory httpFactory,
        LedgerSettings settings,
        ILogger<SessionResetSagaService> logger)
    {
        _httpFactory = httpFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<int> GetOpenPositionsCountAsync(CancellationToken cancellationToken = default)
    {
        using var client = _httpFactory.CreateClient("executor");
        using var response = await client.GetAsync("/api/trading/positions", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Close-all precheck failed: /api/trading/positions returned {StatusCode}", (int)response.StatusCode);
            throw new InvalidOperationException($"Executor positions endpoint failed: {(int)response.StatusCode}");
        }

        var positions = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        if (positions.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return positions.GetArrayLength();
    }

    public async Task<(bool Success, string Message, int ClosedCount)> RequestCloseAllAndWaitAsync(
        Guid accountId,
        string requestedBy,
        CancellationToken cancellationToken = default)
    {
        using var client = _httpFactory.CreateClient("executor");

        var idempotencyKey = $"ledger-reset-{accountId:N}-{Guid.NewGuid():N}";
        var payload = new
        {
            reason = "ledger_session_reset",
            requestedBy,
            confirmationToken = _settings.CloseAllConfirmationToken,
            idempotencyKey
        };

        using var startResponse = await client.PostAsJsonAsync(
            "/api/trading/control/close-all",
            payload,
            cancellationToken);

        if (!startResponse.IsSuccessStatusCode)
        {
            var body = await startResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Close-all request failed for account {AccountId}: HTTP {StatusCode} {Body}",
                accountId,
                (int)startResponse.StatusCode,
                body);

            // Best-effort fallback: operation may already be active due to idempotency or concurrent request.
            return await WaitForCloseAllCompletionAsync(client, cancellationToken);
        }

        return await WaitForCloseAllCompletionAsync(client, cancellationToken);
    }

    private async Task<(bool Success, string Message, int ClosedCount)> WaitForCloseAllCompletionAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(30, _settings.CloseAllTimeoutSeconds));
        var pollInterval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.CloseAllPollIntervalMs, 500, 10_000));
        var startedAt = DateTime.UtcNow;

        while (DateTime.UtcNow - startedAt < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var statusResponse = await client.GetAsync("/api/trading/control/close-all/status", cancellationToken);
            if (!statusResponse.IsSuccessStatusCode)
            {
                await Task.Delay(pollInterval, cancellationToken);
                continue;
            }

            var statusJson = await statusResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var status = statusJson.TryGetProperty("status", out var statusProp)
                ? statusProp.GetString() ?? "Unknown"
                : "Unknown";

            var closedCount = statusJson.TryGetProperty("positionsClosedCount", out var closedProp) && closedProp.TryGetInt32(out var parsedClosed)
                ? parsedClosed
                : 0;

            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return (true, "Close-all completed successfully.", closedCount);
            }

            if (string.Equals(status, "CompletedWithErrors", StringComparison.OrdinalIgnoreCase))
            {
                var error = statusJson.TryGetProperty("lastError", out var errorProp)
                    ? errorProp.GetString() ?? "Close-all completed with errors."
                    : "Close-all completed with errors.";
                return (false, error, closedCount);
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return (false, "Close-all timed out while waiting for completion.", 0);
    }
}
