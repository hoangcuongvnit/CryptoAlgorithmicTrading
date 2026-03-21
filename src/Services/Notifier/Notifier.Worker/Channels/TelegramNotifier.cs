using CryptoTrading.Shared.DTOs;
using Telegram.Bot;

namespace Notifier.Worker.Channels;

public sealed class TelegramNotifier
{
    // Volatile config state — swapped atomically on hot-reload
    private volatile TelegramClientState _state;
    private readonly ILogger<TelegramNotifier> _logger;

    public bool IsEnabled => _state.Enabled;

    public TelegramNotifier(
        string botToken,
        long chatId,
        ILogger<TelegramNotifier> logger)
    {
        _logger = logger;
        _state = BuildState(botToken, chatId, logger);
    }

    /// <summary>Hot-reloads Telegram credentials without restarting the process. Thread-safe.</summary>
    public void Reconfigure(string botToken, long chatId, bool enabled)
    {
        if (!enabled)
        {
            _state = new TelegramClientState(null, 0, false);
            _logger.LogInformation("Telegram notifications disabled via runtime config reload");
            return;
        }

        _state = BuildState(botToken, chatId, _logger);
        _logger.LogInformation("Telegram client reloaded. Enabled={Enabled}", _state.Enabled);
    }

    private static TelegramClientState BuildState(string botToken, long chatId, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(botToken) || chatId == 0)
        {
            logger.LogWarning("Telegram notifications disabled: BotToken or ChatId not configured.");
            return new TelegramClientState(null, 0, false);
        }

        return new TelegramClientState(new TelegramBotClient(botToken), chatId, true);
    }

    public async Task SendStartupNotificationAsync(CancellationToken cancellationToken = default)
    {
        var message = $"🚀 CryptoTrader is ONLINE | {DateTime.UtcNow:HH:mm} UTC";
        await SendMessageAsync(message, cancellationToken);
    }

    public async Task SendSystemEventAsync(SystemEvent evt, CancellationToken cancellationToken = default)
        => await SendMessageAsync(FormatSystemEvent(evt), cancellationToken);

    public static string FormatSystemEvent(SystemEvent evt) => evt.Type switch
    {
        SystemEventType.ServiceStarted => $"🚀 {evt.ServiceName} ONLINE | {evt.Timestamp:HH:mm} UTC",
        SystemEventType.ServiceStopped => $"⏹️ {evt.ServiceName} STOPPED | {evt.Timestamp:HH:mm} UTC",
        SystemEventType.ConnectionLost => $"⚠️ WS DISCONNECTED: {evt.Message} | Reconnecting...",
        SystemEventType.ConnectionRestored => $"✅ WS RESTORED: {evt.Message}",
        SystemEventType.MaxDrawdownBreached => $"🚨 MAX DRAWDOWN BREACHED – All trading halted\n{evt.Message}",
        SystemEventType.Error => $"❌ ERROR in {evt.ServiceName}: {evt.Message}",
        _ => $"ℹ️ {evt.ServiceName}: {evt.Message}"
    };

    public async Task SendOrderResultAsync(OrderResult order, CancellationToken cancellationToken = default)
        => await SendMessageAsync(FormatOrderResult(order), cancellationToken);

    public static string FormatOrderResult(OrderResult order) => order.Success
        ? FormatSuccessfulOrder(order)
        : FormatRejectedOrder(order);

    private static string FormatSuccessfulOrder(OrderResult order)
    {
        var emoji = order.IsPaperTrade ? "📄" : "✅";
        var mode = order.IsPaperTrade ? "PAPER" : "LIVE";
        var side = order.Side.ToString().ToUpperInvariant();

        return $"{emoji} {mode} {side} {order.Symbol} @ ${order.FilledPrice:F2}\n" +
               $"Qty: {order.FilledQty:F6} | Time: {order.Timestamp:HH:mm:ss} UTC";
    }

    private static string FormatRejectedOrder(OrderResult order)
    {
        return $"❌ REJECTED {order.Symbol}\n" +
               $"Reason: {order.ErrorMessage}\n" +
               $"Time: {order.Timestamp:HH:mm:ss} UTC";
    }

    private async Task SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        var state = _state;
        if (!state.Enabled)
        {
            _logger.LogDebug("[Telegram disabled] {Message}", message[..Math.Min(50, message.Length)]);
            return;
        }

        try
        {
            await state.Client!.SendMessage(
                chatId: state.ChatId,
                text: message,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Sent Telegram notification: {Message}", message[..Math.Min(50, message.Length)]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram notification");
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var state = _state;
        if (!state.Enabled)
        {
            _logger.LogWarning("Telegram connection test skipped: notifications disabled.");
            return false;
        }

        try
        {
            var me = await state.Client!.GetMe(cancellationToken);
            _logger.LogInformation("Telegram bot connected: @{Username}", me.Username);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Telegram");
            return false;
        }
    }

    /// <summary>Sends an arbitrary message using the current active config. Used by test-message endpoint.</summary>
    public Task SendDirectMessageAsync(string message, CancellationToken cancellationToken = default)
        => SendMessageAsync(message, cancellationToken);

    /// <summary>Validates credentials by creating a temporary client. Does not modify current state.</summary>
    public static async Task<(bool Valid, string? BotUsername, string Message)> ValidateCredentialsAsync(
        string botToken,
        long chatId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(botToken) || chatId == 0)
            return (false, null, "Bot token and chat ID are required");

        try
        {
            var tempClient = new TelegramBotClient(botToken);
            var me = await tempClient.GetMe(cancellationToken);
            // Attempt to send a silent probe to verify chat reachability
            bool chatReachable;
            try
            {
                await tempClient.SendMessage(chatId: chatId, text: "\u2705 Telegram connected",
                    cancellationToken: cancellationToken);
                chatReachable = true;
            }
            catch
            {
                chatReachable = false;
            }

            var msg = chatReachable
                ? "Connection successful"
                : $"Bot token valid (@{me.Username}) but chat not reachable. Ensure the bot was started in this chat.";
            return (chatReachable, me.Username, msg);
        }
        catch (Exception ex)
        {
            return (false, null, $"Invalid token: {ex.Message}");
        }
    }
}

/// <summary>Immutable snapshot of active Telegram client configuration.</summary>
internal sealed record TelegramClientState(TelegramBotClient? Client, long ChatId, bool Enabled);
