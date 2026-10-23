namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// The producer side of an enrichment stage in front of the batching worker: an item is handed over
/// without waiting, enriched in the background and then queued for the batching worker.
/// </summary>
/// <typeparam name="TItem">The item the pipeline carries.</typeparam>
/// <remarks>
/// A producer injects either this or <see cref="IBoundedBuffer{TItem}"/>, depending on whether the
/// item is enriched on its way. When the stage is full the item is not dropped: it goes to the
/// batching worker raw and the skip is counted, so the bound costs enrichment and not the item.
/// </remarks>
public interface IEnrichmentStage<in TItem>
{
    /// <summary>Hands <paramref name="item"/> over for enrichment without ever waiting.</summary>
    /// <param name="item">The item to enrich and queue.</param>
    /// <returns><c>true</c> when the item awaits enrichment; <c>false</c> when the stage is full or stopped and the item went on raw.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is null.</exception>
    bool TryEnqueue(TItem item);
}
