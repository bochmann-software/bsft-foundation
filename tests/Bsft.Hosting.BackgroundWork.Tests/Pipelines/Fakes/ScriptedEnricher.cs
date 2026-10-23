using Bsft.Hosting.BackgroundWork.Pipelines;

namespace Bsft.Hosting.BackgroundWork.Tests.Pipelines.Fakes;

/// <summary>An enricher whose behavior a test scripts.</summary>
internal sealed class ScriptedEnricher : IPipelineEnricher<SampleItem>
{
    /// <summary>Gets or sets what the enrichment does.</summary>
    public Func<SampleItem, CancellationToken, ValueTask<SampleItem?>> Behavior { get; set; } =
        static (item, _) => ValueTask.FromResult<SampleItem?>(item with { Tag = "enriched" });

    /// <inheritdoc />
    public ValueTask<SampleItem?> EnrichAsync(SampleItem item, CancellationToken cancellationToken) =>
        Behavior(item, cancellationToken);
}
