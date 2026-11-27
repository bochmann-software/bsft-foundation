namespace Bsft.Mediator;

/// <summary>
/// The rest of a request's pipeline after a rescoping behavior, as the mediator built it: run in the
/// request's own scope, or rebuilt in a fresh one.
/// </summary>
/// <typeparam name="TResponse">The type of the response.</typeparam>
/// <remarks>
/// The mediator owns the scopes and the resolution of behaviors and handlers; this type only hands a
/// behavior's attempt to the one or the other run it was built with.
/// </remarks>
internal sealed class PipelineContinuation<TResponse> : IPipelineContinuation<TResponse>
{
    private readonly Func<PipelineAttempt<TResponse>, Task<TResponse>> _runInRequestScope;
    private readonly Func<PipelineAttempt<TResponse>, Task<TResponse>> _runInNewScope;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineContinuation{TResponse}"/> class.
    /// </summary>
    /// <param name="runInRequestScope">Hands an attempt the request's scope and the rest built in it.</param>
    /// <param name="runInNewScope">Hands an attempt a fresh, seeded scope and the rest rebuilt in it.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public PipelineContinuation(
        Func<PipelineAttempt<TResponse>, Task<TResponse>> runInRequestScope,
        Func<PipelineAttempt<TResponse>, Task<TResponse>> runInNewScope)
    {
        _runInRequestScope = runInRequestScope ?? throw new ArgumentNullException(nameof(runInRequestScope));
        _runInNewScope = runInNewScope ?? throw new ArgumentNullException(nameof(runInNewScope));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attempt"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public Task<TResponse> RunAsync(
        PipelineAttempt<TResponse> attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        cancellationToken.ThrowIfCancellationRequested();

        return _runInRequestScope(attempt);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attempt"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public Task<TResponse> RunInNewScopeAsync(
        PipelineAttempt<TResponse> attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        cancellationToken.ThrowIfCancellationRequested();

        return _runInNewScope(attempt);
    }
}
