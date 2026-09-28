using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Bsft.Resilience.Mediator;

/// <summary>
/// Builds the mediator resilience pipeline once from <see cref="BsftResilienceMediatorOptions"/>
/// and exposes it for reuse. Registered as a singleton so the (relatively expensive) pipeline
/// build happens a single time rather than per request.
/// </summary>
internal sealed class MediatorResiliencePipelineProvider : IMediatorResiliencePipelineProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorResiliencePipelineProvider"/> class.
    /// </summary>
    /// <param name="options">The mediator resilience options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the options carry no retry predicate.</exception>
    public MediatorResiliencePipelineProvider(IOptions<BsftResilienceMediatorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;

        if (value.ShouldRetry is null)
            throw new ArgumentException($"{nameof(BsftResilienceMediatorOptions.ShouldRetry)} must not be null.", nameof(options));

        Pipeline = Build(value);
    }

    /// <inheritdoc />
    public ResiliencePipeline Pipeline { get; }

    private static ResiliencePipeline Build(BsftResilienceMediatorOptions options) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = options.BaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => new ValueTask<bool>(options.ShouldRetry(args.Outcome.Exception)),
            })
            .Build();
}
