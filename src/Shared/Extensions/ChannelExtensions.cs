using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace CryptoTrading.Shared.Extensions;

/// <summary>
/// Extension methods for System.Threading.Channels
/// </summary>
public static class ChannelExtensions
{
    /// <summary>
    /// Reads all items from a channel asynchronously
    /// </summary>
    public static async IAsyncEnumerable<T> ReadAllAsync<T>(
        this ChannelReader<T> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    /// <summary>
    /// Tries to write an item to a channel, returns false if channel is full
    /// </summary>
    public static bool TryWrite<T>(this ChannelWriter<T> writer, T item)
    {
        return writer.TryWrite(item);
    }
}
