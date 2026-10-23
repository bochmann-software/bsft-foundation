namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>The writing side of <see cref="IPipelineCounters{TItem}"/>, which only the pipeline's own parts hold.</summary>
/// <typeparam name="TItem">The item the pipeline carries.</typeparam>
internal interface IPipelineCounterSink<TItem> : IPipelineCounters<TItem>
{
    /// <summary>Counts one accepted item.</summary>
    void CountEnqueued();

    /// <summary>Counts one refused item.</summary>
    void CountDropped();

    /// <summary>Counts committed items.</summary>
    /// <param name="count">How many.</param>
    void CountCommitted(int count);

    /// <summary>Counts items lost with a failed batch.</summary>
    /// <param name="count">How many items.</param>
    void CountFailedBatch(int count);

    /// <summary>Counts items that were left behind when the stop deadline passed.</summary>
    /// <param name="count">How many items.</param>
    void CountAbandoned(int count);

    /// <summary>Counts one failed enrichment.</summary>
    void CountEnrichmentFailed();

    /// <summary>Counts items that skipped enrichment.</summary>
    /// <param name="count">How many items.</param>
    void CountEnrichmentSkipped(int count);
}
