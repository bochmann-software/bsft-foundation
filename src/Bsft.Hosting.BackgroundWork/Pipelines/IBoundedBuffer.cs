namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// The producer side of a bounded, never-blocking buffer: a hot path hands an item over and carries on,
/// whether or not anything downstream keeps up.
/// </summary>
/// <typeparam name="TItem">The item a producer hands over.</typeparam>
/// <remarks>
/// Losing the item is the price of never waiting. A caller that must not lose it uses a store with a
/// transaction, not this buffer; a caller that must not wait on the store, such as a request that also
/// records a usage measurement, uses this one and reads <see cref="IPipelineCounters{TItem}.Dropped"/>
/// to see what the bound cost.
/// </remarks>
public interface IBoundedBuffer<in TItem>
{
    /// <summary>Hands <paramref name="item"/> over without ever waiting.</summary>
    /// <param name="item">The item to queue.</param>
    /// <returns><c>true</c> when the item was queued; <c>false</c> when the buffer is full or no longer accepts items, in which case the drop is counted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is null.</exception>
    bool TryEnqueue(TItem item);
}
