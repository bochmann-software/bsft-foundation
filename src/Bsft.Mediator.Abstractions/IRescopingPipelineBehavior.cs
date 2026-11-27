namespace Bsft.Mediator;

/// <summary>
/// A pipeline behavior that may run the rest of the pipeline more than once, and each time in a fresh
/// dependency-injection scope — the retry behavior over the shared transaction.
/// </summary>
/// <typeparam name="TRequest">The type of the request.</typeparam>
/// <typeparam name="TResponse">The type of the response.</typeparam>
/// <remarks>
/// The mediator calls <see cref="HandleAsync(TRequest, IPipelineContinuation{TResponse}, CancellationToken)"/>
/// instead of the delegate overload when a behavior implements this interface. The delegate overload
/// stays the contract for a pipeline that cannot rebuild its rest in another scope.
/// </remarks>
public interface IRescopingPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    /// <summary>
    /// Handles the request, running the rest of the pipeline through <paramref name="next"/>.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="next">The rest of the pipeline, in the request's scope or in a fresh one.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The response from the pipeline.</returns>
    Task<TResponse> HandleAsync(TRequest request, IPipelineContinuation<TResponse> next, CancellationToken cancellationToken = default);
}
