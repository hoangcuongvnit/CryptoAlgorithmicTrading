using Microsoft.AspNetCore.SignalR;

namespace FinancialLedger.Worker.Hubs;

public sealed class LedgerHub : Hub
{
    private readonly ILogger<LedgerHub> _logger;

    public LedgerHub(ILogger<LedgerHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Ledger client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Ledger client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
