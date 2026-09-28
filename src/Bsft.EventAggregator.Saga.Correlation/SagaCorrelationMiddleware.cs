using Bsft.Correlation;
using Bsft.EventAggregator.Saga.Timeouts;
using Microsoft.AspNetCore.Http;

namespace Bsft.EventAggregator.Saga.Correlation;

/// <summary>
/// Makes the correlation id of the current request ambient for the saga engine, so that a saga step
/// triggered by an event the request publishes runs under the request's id instead of one minted for
/// the step's own scope.
/// </summary>
/// <remarks>
/// <para>
/// A step runs in a service scope created from the root provider, so it cannot see the request's
/// scoped <see cref="ICorrelationContext"/>. The timeout path already solves the same problem by
/// restoring the scheduling request's id into <see cref="ISagaCorrelationScope"/> while the timeout
/// publishes; this middleware does the equivalent for the direct event to step path, and the flow
/// host restores whichever of the two is ambient into the step scope.
/// </para>
/// <para>
/// Place it after the correlation middleware, which is what resolves the id for the request.
/// Without this middleware nothing breaks: a step simply keeps the fresh id of its own scope.
/// </para>
/// </remarks>
public sealed class SagaCorrelationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ISagaCorrelationScope _sagaCorrelationScope;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaCorrelationMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="sagaCorrelationScope">The ambient scope the saga engine reads.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public SagaCorrelationMiddleware(RequestDelegate next, ISagaCorrelationScope sagaCorrelationScope)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _sagaCorrelationScope = sagaCorrelationScope ?? throw new ArgumentNullException(nameof(sagaCorrelationScope));
    }

    /// <summary>Invokes the middleware for the current HTTP context.</summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <param name="correlationContext">The correlation context resolved for this request.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (correlationContext is null)
            throw new ArgumentNullException(nameof(correlationContext));

        using (_sagaCorrelationScope.Use(correlationContext.CorrelationId))
        {
            await _next(context).ConfigureAwait(false);
        }
    }
}
