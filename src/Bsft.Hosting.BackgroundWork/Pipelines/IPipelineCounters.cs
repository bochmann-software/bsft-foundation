namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// What a pipeline of <typeparamref name="TItem"/> has done so far, readable by a health check or a test
/// and mirrored to the <c>Bsft.Hosting</c> instrumentation source as <c>bsft.pipeline.items</c> with the
/// tags <c>pipeline</c> and <c>outcome</c>.
/// </summary>
/// <typeparam name="TItem">The item the pipeline carries.</typeparam>
public interface IPipelineCounters<TItem>
{
    /// <summary>Gets how many items the buffer accepted.</summary>
    long Enqueued { get; }

    /// <summary>Gets how many items the buffer refused because it was full or stopped.</summary>
    long Dropped { get; }

    /// <summary>Gets how many items were committed.</summary>
    long Committed { get; }

    /// <summary>Gets how many items were lost with a batch that failed, or were left at the stop deadline.</summary>
    long Failed { get; }

    /// <summary>Gets how many batches failed.</summary>
    long FailedBatches { get; }

    /// <summary>Gets how many enrichments faulted, returned nothing or were given up on.</summary>
    long EnrichmentFailed { get; }

    /// <summary>Gets how many items skipped enrichment because the stage was full or already stopped, or were passed on raw when the stage's stop budget ran out.</summary>
    long EnrichmentSkipped { get; }
}
