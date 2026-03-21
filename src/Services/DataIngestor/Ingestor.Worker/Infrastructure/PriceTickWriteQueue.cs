using CryptoTrading.Shared.DTOs;
using Ingestor.Worker.Configuration;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace Ingestor.Worker.Infrastructure;

public sealed class PriceTickWriteQueue
{
    private readonly Channel<PriceTick> _channel;

    public PriceTickWriteQueue(IOptions<PersistenceSettings> settings)
    {
        var options = new BoundedChannelOptions(settings.Value.BufferCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        };

        _channel = Channel.CreateBounded<PriceTick>(options);
    }

    public ValueTask EnqueueAsync(PriceTick tick, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(tick, cancellationToken);
    }

    public ChannelReader<PriceTick> Reader => _channel.Reader;
}
