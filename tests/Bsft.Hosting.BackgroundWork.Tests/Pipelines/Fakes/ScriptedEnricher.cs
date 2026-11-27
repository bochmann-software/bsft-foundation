using Bsft.Hosting.BackgroundWork.Pipelines;

namespace Bsft.Hosting.BackgroundWork.Tests.Pipelines.Fakes;

/// <summary>An enricher whose behavior a test scripts.</summary>
internal sealed class ScriptedEnricher : IPipelineEnricher<SampleItem>
{
    private int _calls;

    /// <summary>Gets or sets what the enrichment does.</summary>
    public Func<SampleItem, CancellationToken, ValueTask<SampleItem?>> Behavior { get; set; } =
        static (item, _) => ValueTask.FromResult<SampleItem?>(item with { Tag = "enriched" });

    /// <summary>Gets how many times the enricher was asked to enrich an item.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <inheritdoc />
    public ValueTask<SampleItem?> EnrichAsync(SampleItem item, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);

        return Behavior(item, cancellationToken);
    }
}
