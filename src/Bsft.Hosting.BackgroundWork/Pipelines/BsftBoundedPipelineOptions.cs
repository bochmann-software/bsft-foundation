namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// What an operator decides about one bounded pipeline: how much it holds, how it batches, how long the
/// final flush may take and how long one enrichment may run.
/// </summary>
/// <remarks>
/// The options are read once, when the pipeline is built; a change to the configuration after that is
/// not applied until the host restarts.
/// Registered as <em>named</em> options, keyed by the item type's full name, so a host that runs
/// pipelines for several item types configures each independently. The configuration overloads of
/// <c>AddBsftBatchingWriter</c> bind <c>Bsft:Hosting:Pipelines:&lt;item type full name&gt;</c>.
/// </remarks>
public sealed class BsftBoundedPipelineOptions
{
    /// <summary>The configuration section the per-item sections sit under.</summary>
    public const string SectionName = "Bsft:Hosting:Pipelines";

    /// <summary>The name the options of a pipeline of <typeparamref name="TItem"/> are registered under.</summary>
    /// <typeparam name="TItem">The item the pipeline carries.</typeparam>
    /// <returns>The item type's full name, or its short name where the runtime gives no full one.</returns>
    public static string NameFor<TItem>() => typeof(TItem).FullName ?? typeof(TItem).Name;

    /// <summary>Gets or sets how many items wait in the buffer at most. Defaults to 1000.</summary>
    public int Capacity { get; set; } = 1_000;

    /// <summary>Gets or sets how many items one commit carries at most. Defaults to 100.</summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets how long a batch waits for more items once it has its first. Defaults to one second.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets how long the final flush may take after the host asks the worker to stop. Defaults to
    /// five seconds. Items still queued when it passes are counted as failed.
    /// </summary>
    public TimeSpan FlushOnStopTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets how long the enrichment stage keeps enriching what is queued once the host asks it to
    /// stop. Defaults to five seconds. Whatever is still queued when it passes, or when the host's own stop
    /// token fires, goes on raw, so the stage cannot use up the shutdown time the batching writer needs.
    /// </summary>
    public TimeSpan EnrichmentOnStopTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets whether an enrichment stage is registered for this pipeline; set by <c>AddBsftPipelineEnrichment</c>, never bound from configuration.</summary>
    internal bool HasEnrichmentStage { get; set; }

    /// <summary>Gets or sets how many items wait for enrichment at most. Defaults to 1000.</summary>
    public int EnrichmentCapacity { get; set; } = 1_000;

    /// <summary>Gets or sets how long one enrichment may take before it is given up on. Defaults to 30 seconds.</summary>
    public TimeSpan EnrichmentTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
