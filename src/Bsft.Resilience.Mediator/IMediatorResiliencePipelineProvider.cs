using Polly;

namespace Bsft.Resilience.Mediator;

/// <summary>
/// Provides the shared, pre-built resilience pipeline used by <see cref="RetryBehavior{TRequest, TResponse}"/>.
/// </summary>
public interface IMediatorResiliencePipelineProvider
{
    /// <summary>
    /// Gets the resilience pipeline. The pipeline is built once and is safe to reuse concurrently.
    /// </summary>
    ResiliencePipeline Pipeline { get; }
}
