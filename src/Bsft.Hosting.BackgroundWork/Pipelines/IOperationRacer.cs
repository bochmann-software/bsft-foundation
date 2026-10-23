namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>
/// Runs an operation as a task of its own and gives up on it when a time limit or an abort token passes,
/// even if the operation ignores the token it was handed.
/// </summary>
/// <remarks>
/// Cooperative cancellation alone lets one operation that never looks at its token hold a hosted
/// service, and with it the host's shutdown, for as long as it likes. The racer waits for the operation
/// <em>or</em> the give-up signal, whichever comes first, so the caller never waits on an operation that
/// does not finish.
/// </remarks>
internal interface IOperationRacer
{
    /// <summary>Runs <paramref name="operation"/> and waits for it, the limit or the abort token.</summary>
    /// <typeparam name="TValue">The value the operation produces.</typeparam>
    /// <param name="operation">The operation; a throw before its first await is a fault, not an escape.</param>
    /// <param name="limit">How long the operation may take; <see cref="Timeout.InfiniteTimeSpan"/> sets none.</param>
    /// <param name="abort">Gives up on the operation when cancelled; an operation started under a cancelled token is not started.</param>
    /// <returns>How the operation ended.</returns>
    Task<RacedResult<TValue>> RunAsync<TValue>(Func<CancellationToken, Task<TValue>> operation, TimeSpan limit, CancellationToken abort);
}
