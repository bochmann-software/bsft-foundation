namespace Bsft.Mediator;

/// <summary>
/// The rest of a request's pipeline after an <see cref="IRescopingPipelineBehavior{TRequest, TResponse}"/>
/// — the later behaviors and the handler — which can run in the request's own scope or in a fresh one.
/// </summary>
/// <typeparam name="TResponse">The type of the response.</typeparam>
/// <remarks>
/// Both methods hand the given attempt the provider of the scope the rest runs in, and the call that
/// runs it, so the behavior can read that scope's services before and after the rest ran — while the
/// scope is still alive.
/// </remarks>
public interface IPipelineContinuation<TResponse>
{
    /// <summary>Runs the rest of the pipeline in the request's own scope.</summary>
    /// <param name="attempt">
    /// Given the provider of the request's scope and the call that runs the rest of the pipeline in it.
    /// </param>
    /// <param name="cancellationToken">Stops the run before it starts.</param>
    /// <returns>What <paramref name="attempt"/> returns.</returns>
    Task<TResponse> RunAsync(
        PipelineAttempt<TResponse> attempt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the rest of the pipeline in a fresh scope: the scope is created, every registered
    /// <see cref="IRequestScopeSeed"/> seeds it, the later behaviors and the handler are resolved from
    /// it, and it is disposed once <paramref name="attempt"/> completed.
    /// </summary>
    /// <param name="attempt">
    /// Given the provider of the fresh scope and the call that runs the rest of the pipeline in it.
    /// </param>
    /// <param name="cancellationToken">Stops the run before the fresh scope is created.</param>
    /// <returns>What <paramref name="attempt"/> returns.</returns>
    Task<TResponse> RunInNewScopeAsync(
        PipelineAttempt<TResponse> attempt, CancellationToken cancellationToken = default);
}
