namespace Executor.API.Infrastructure;

/// <summary>
/// Coordinates startup ordering between CredentialSyncService (producer)
/// and StartupReconciliationService (consumer).
/// CredentialSyncService calls Complete() when done (success or graceful fallback).
/// StartupReconciliationService awaits WhenComplete before making any Binance calls.
/// </summary>
public sealed class CredentialSyncGate
{
    private readonly TaskCompletionSource<bool> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WhenComplete => _tcs.Task;

    /// <summary>Called by CredentialSyncService in a finally block — always signals, never throws.</summary>
    public void Complete() => _tcs.TrySetResult(true);
}
