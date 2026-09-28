using Bsft.Mediator;
using System.Collections.Concurrent;
using System.Reflection;

namespace Bsft.Resilience.Mediator;

/// <summary>
/// Mediator pipeline behavior that retries requests marked with <see cref="RetryableAttribute"/> on
/// transient failures, using the shared resilience pipeline. Requests without the marker pass
/// through untouched, so the behavior adds no overhead to non-retryable requests.
/// </summary>
/// <typeparam name="TRequest">The type of the request.</typeparam>
/// <typeparam name="TResponse">The type of the response.</typeparam>
public sealed class RetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    // The pipeline dispatches with the declared request contract as TRequest, so the marker has to be
    // read from the runtime type. The result is cached per type to keep reflection off the hot path.
    private static readonly ConcurrentDictionary<Type, bool> RetryableTypes = new();

    private readonly IMediatorResiliencePipelineProvider _pipelineProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="pipelineProvider">The provider of the shared resilience pipeline.</param>
    public RetryBehavior(IMediatorResiliencePipelineProvider pipelineProvider)
    {
        _pipelineProvider = pipelineProvider ?? throw new ArgumentNullException(nameof(pipelineProvider));
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default)
    {
        if (next is null)
            throw new ArgumentNullException(nameof(next));

        if (request is null || !IsRetryable(request.GetType()))
            return await next().ConfigureAwait(false);

        // Pass next as Polly state with a non-async static callback so a retryable request neither
        // allocates a closure capturing next nor spins up an extra async state machine per attempt;
        // the handler's Task is wrapped directly in a ValueTask.
        return await _pipelineProvider.Pipeline
            .ExecuteAsync(static (handler, _) => new ValueTask<TResponse>(handler()), next, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsRetryable(Type requestType) =>
        RetryableTypes.GetOrAdd(
            requestType,
            static type => type.GetCustomAttribute<RetryableAttribute>(inherit: true) is not null);
}
