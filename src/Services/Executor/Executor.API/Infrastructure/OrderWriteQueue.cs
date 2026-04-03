using CryptoTrading.Shared.DTOs;
using System.Threading.Channels;

namespace Executor.API.Infrastructure;

public sealed class OrderWriteQueue
{
    private const int ChannelCapacity = 2_000;

    private readonly Channel<(OrderRequest Request, OrderResult Result)> _channel;
    private readonly ILogger<OrderWriteQueue> _logger;

    public OrderWriteQueue(ILogger<OrderWriteQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<(OrderRequest, OrderResult)>(
            new BoundedChannelOptions(ChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });
    }

    /// <summary>
    /// Non-blocking enqueue. Never delays the gRPC caller.
    /// Returns false (and logs a warning) only when the channel is saturated.
    /// </summary>
    public bool TryEnqueue(OrderRequest request, OrderResult result)
    {
        var accepted = _channel.Writer.TryWrite((request, result));
        if (!accepted)
            _logger.LogWarning(
                "OrderWriteQueue is full ({Capacity}); oldest record dropped for {Symbol}",
                ChannelCapacity, request.Symbol);
        return accepted;
    }

    public ChannelReader<(OrderRequest Request, OrderResult Result)> Reader => _channel.Reader;
}
