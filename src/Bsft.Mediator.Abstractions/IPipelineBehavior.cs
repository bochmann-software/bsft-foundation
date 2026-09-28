namespace Bsft.Mediator;

/// <summary>
/// Defines a pipeline behavior that wraps command and query execution.
/// Behaviors are executed in the order they are registered and form a chain around the handler.
/// </summary>
/// <typeparam name="TRequest">The type of the request being handled.</typeparam>
/// <typeparam name="TResponse">The type of the response produced by the handler.</typeparam>
public interface IPipelineBehavior<TRequest, TResponse>
{
    /// <summary>
    /// Handles the request by optionally performing work before and after calling the next delegate in the pipeline.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="next">A delegate representing the next step in the pipeline.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The response from the pipeline.</returns>
    Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default);
}
