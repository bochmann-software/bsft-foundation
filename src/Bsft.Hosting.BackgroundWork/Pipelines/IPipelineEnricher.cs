namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// Adds to one item before it reaches the batching worker: a lookup, a classification, a call that is
/// too slow for the producer's hot path.
/// </summary>
/// <typeparam name="TItem">The item the pipeline carries.</typeparam>
/// <remarks>
/// The stage never trusts an enricher. A fault, a <c>null</c> result and a call that outlives
/// <see cref="BsftBoundedPipelineOptions.EnrichmentTimeout"/> or the host's stop all count as a failed
/// enrichment, and the raw item goes on instead, so an enricher may throw freely and need not honor its
/// token for the host to stop.
/// </remarks>
public interface IPipelineEnricher<TItem>
    where TItem : class
{
    /// <summary>Enriches <paramref name="item"/>.</summary>
    /// <param name="item">The raw item.</param>
    /// <param name="cancellationToken">Cancelled when the enrichment times out or the host stops.</param>
    /// <returns>The enriched item; <c>null</c> counts as a failure and the raw item goes on.</returns>
    ValueTask<TItem?> EnrichAsync(TItem item, CancellationToken cancellationToken);
}
