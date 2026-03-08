using Telegram.Bot;
using Telegram.Bot.Types;
using CryptoTrading.Shared.DTOs;

namespace Notifier.Worker.Channels;

public sealed class TelegramNotifier
{
    private readonly TelegramBotClient? _botClient;
    private readonly long _chatId;
    private readonly ILogger<TelegramNotifier> _logger;
    private readonly bool _enabled;

    public TelegramNotifier(
        string botToken,
        long chatId,
        ILogger<TelegramNotifier> logger)
    {
        _chatId = chatId;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(botToken) || chatId == 0)
        {
            _enabled = false;
            _logger.LogWarning("Telegram notifications disabled: BotToken or ChatId not configured.");
            return;
        }

        _botClient = new TelegramBotClient(botToken);
        _enabled = true;
    }

    public async Task SendStartupNotificationAsync(CancellationToken cancellationToken = default)
    {
        var message = $"🚀 CryptoTrader is ONLINE | {DateTime.UtcNow:HH:mm} UTC";
        await SendMessageAsync(message, cancellationToken);
    }

    public async Task SendSystemEventAsync(SystemEvent evt, CancellationToken cancellationToken = default)
    {
        var message = evt.Type switch
        {
            SystemEventType.ServiceStarted => $"🚀 {evt.ServiceName} ONLINE | {evt.Timestamp:HH:mm} UTC",
            SystemEventType.ServiceStopped => $"⏹️ {evt.ServiceName} STOPPED | {evt.Timestamp:HH:mm} UTC",
            SystemEventType.ConnectionLost => $"⚠️ WS DISCONNECTED: {evt.Message} | Reconnecting...",
            SystemEventType.ConnectionRestored => $"✅ WS RESTORED: {evt.Message}",
            SystemEventType.MaxDrawdownBreached => $"🚨 MAX DRAWDOWN BREACHED – All trading halted\n{evt.Message}",
            SystemEventType.Error => $"❌ ERROR in {evt.ServiceName}: {evt.Message}",
            _ => $"ℹ️ {evt.ServiceName}: {evt.Message}"
        };

        await SendMessageAsync(message, cancellationToken);
    }

    public async Task SendOrderResultAsync(OrderResult order, CancellationToken cancellationToken = default)
    {
        var message = order.Success
            ? FormatSuccessfulOrder(order)
            : FormatRejectedOrder(order);

        await SendMessageAsync(message, cancellationToken);
    }

    private static string FormatSuccessfulOrder(OrderResult order)
    {
        var emoji = order.IsPaperTrade ? "📄" : "✅";
        var mode = order.IsPaperTrade ? "PAPER" : "LIVE";
        var side = order.Symbol.Contains("BUY", StringComparison.OrdinalIgnoreCase) ? "BUY" : "SELL";

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
        if (!_enabled)
        {
            _logger.LogDebug("[Telegram disabled] {Message}", message.Substring(0, Math.Min(50, message.Length)));
            return;
        }

        try
        {
            await _botClient!.SendMessage(
                chatId: _chatId,
                text: message,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Sent Telegram notification: {Message}", message.Substring(0, Math.Min(50, message.Length)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram notification");
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            _logger.LogWarning("Telegram connection test skipped: notifications disabled.");
            return false;
        }

        try
        {
            var me = await _botClient!.GetMe(cancellationToken);
            _logger.LogInformation("Telegram bot connected: @{Username}", me.Username);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Telegram");
            return false;
        }
    }
}
