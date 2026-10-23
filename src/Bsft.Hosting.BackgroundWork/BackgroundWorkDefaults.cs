namespace Bsft.Hosting.BackgroundWork;

/// <summary>The constants a host needs to name what the background-work pipelines emit.</summary>
public static class BackgroundWorkDefaults
{
    /// <summary>
    /// The name of the instrumentation source the bounded pipelines of
    /// <c>Bsft.Hosting.BackgroundWork.Pipelines</c> emit their <c>bsft.pipeline.items</c> counter on.
    /// </summary>
    /// <remarks>
    /// The name is <c>Bsft.Hosting</c>, the source the pipelines emitted on while they lived in the web
    /// host package, so a collector that already subscribes to it keeps receiving the counter.
    /// </remarks>
    public const string PipelineInstrumentationSourceName = "Bsft.Hosting";
}
