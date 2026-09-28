namespace Bsft.Scheduling.Execution;

/// <summary>
/// Runs a single claimed job run: resolves the job from its own dependency injection scope, restores
/// the correlation id and measures the execution.
/// </summary>
/// <remarks>
/// Separated from the dispatcher so that a host can decorate it — with a resilience pipeline or an
/// audit hook — without reimplementing the polling loop.
/// </remarks>
public interface IJobExecutor
{
    /// <summary>
    /// Executes one claimed run.
    /// </summary>
    /// <param name="jobType">The <see cref="IJob"/> implementation to resolve and invoke.</param>
    /// <param name="run">The claimed run.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the job has finished.</returns>
    Task ExecuteAsync(Type jobType, JobRun run, CancellationToken cancellationToken = default);
}
