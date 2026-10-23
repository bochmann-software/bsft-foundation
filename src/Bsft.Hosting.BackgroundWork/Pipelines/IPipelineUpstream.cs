namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// What feeds a batching writer from upstream, so the writer can wait for it to finish before it stops
/// accepting items, instead of relying on the host stopping the two in a particular order.
/// </summary>
/// <typeparam name="TItem">The item the pipeline carries.</typeparam>
internal interface IPipelineUpstream<TItem>
{
    /// <summary>Gets a task that completes when the upstream has handed on everything it will ever hand on.</summary>
    Task Completion { get; }
}
