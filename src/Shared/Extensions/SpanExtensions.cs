namespace CryptoTrading.Shared.Extensions;

/// <summary>
/// Extension methods for working with Span&lt;T&gt; in indicator calculations
/// </summary>
public static class SpanExtensions
{
    /// <summary>
    /// Converts a list of decimals to an array (for indicator library compatibility)
    /// </summary>
    public static decimal[] ToDecimalArray(this IEnumerable<decimal> source) => source.ToArray();

    /// <summary>
    /// Gets the last N elements from a span
    /// </summary>
    public static ReadOnlySpan<T> TakeLast<T>(this ReadOnlySpan<T> span, int count)
    {
        if (count >= span.Length)
            return span;

        return span.Slice(span.Length - count, count);
    }

    /// <summary>
    /// Calculates simple moving average over a span
    /// </summary>
    public static decimal SimpleMovingAverage(this ReadOnlySpan<decimal> values)
    {
        if (values.Length == 0)
            return 0;

        decimal sum = 0;
        foreach (var value in values)
        {
            sum += value;
        }

        return sum / values.Length;
    }
}
