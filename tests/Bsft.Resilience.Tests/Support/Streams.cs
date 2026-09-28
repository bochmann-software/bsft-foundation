using System.Runtime.CompilerServices;

namespace Bsft.Resilience.Tests.Support;

/// <summary>The stream shapes the retry tests need: one that ends, one that breaks, and a collector.</summary>
internal static class Streams
{
    /// <summary>Yields <paramref name="count"/> items starting at <paramref name="start"/>, then ends.</summary>
    /// <param name="start">The first value.</param>
    /// <param name="count">How many values to yield.</param>
    /// <param name="cancellationToken">Stops the enumeration.</param>
    /// <returns>The stream.</returns>
    public static async IAsyncEnumerable<int> Range(
        int start, int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return start + i;
        }
    }

    /// <summary>Yields <paramref name="items"/> values from zero and then throws <paramref name="failure"/>.</summary>
    /// <param name="items">How many values to yield before failing; zero fails before the first item.</param>
    /// <param name="failure">What to throw.</param>
    /// <param name="cancellationToken">Stops the enumeration.</param>
    /// <returns>The stream.</returns>
    public static async IAsyncEnumerable<int> ThrowAfter(
        int items, Exception failure, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < items; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }

        await Task.Yield();
        throw failure;
    }

    /// <summary>Drains a stream into a list.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="source">The stream to drain.</param>
    /// <returns>Everything the stream yielded.</returns>
    public static async Task<List<T>> Collect<T>(this IAsyncEnumerable<T> source)
    {
        var items = new List<T>();

        await foreach (var item in source.ConfigureAwait(false))
            items.Add(item);

        return items;
    }
}
