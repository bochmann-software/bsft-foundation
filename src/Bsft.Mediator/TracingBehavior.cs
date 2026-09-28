namespace Bsft.Mediator;

/// <summary>
/// Pipeline behavior that records a trace span and metrics for each command and query.
/// Registered as the outermost behavior when tracing is enabled via
/// <see cref="MediatorConfiguration.EnableTracing"/> so the span wraps the whole pipeline.
/// </summary>
/// <typeparam name="TRequest">The type of the request.</typeparam>
/// <typeparam name="TResponse">The type of the response.</typeparam>
public sealed class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly IMediatorTelemetry _telemetry;
    private readonly ICorrelationIdAccessor? _correlationIdAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="TracingBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="telemetry">The mediator telemetry emitter.</param>
    /// <param name="correlationIdAccessor">An optional accessor providing the current correlation ID.</param>
    public TracingBehavior(IMediatorTelemetry telemetry, ICorrelationIdAccessor? correlationIdAccessor = null)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _correlationIdAccessor = correlationIdAccessor;
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default)
    {
        if (next is null)
            throw new ArgumentNullException(nameof(next));

        // The mediator dispatches under the declared contract, so the closed generic would name
        // every command of one result type alike; a span has to carry the request the caller sent.
        var requestType = request?.GetType() ?? typeof(TRequest);

        using var scope = _telemetry.BeginRequest(
            requestType.Name,
            requestType.ToString(),
            _correlationIdAccessor?.CorrelationId);

        try
        {
            var response = await next().ConfigureAwait(false);
            scope.Complete();
            return response;
        }
        // Record the failure outcome, then rethrow unchanged. Nothing is swallowed.
        catch (Exception ex)
        {
            scope.Fail(ex);
            throw;
        }
    }
}
