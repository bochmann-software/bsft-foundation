namespace Bsft.Scheduling;

/// <summary>
/// A unit of background work that the scheduler can run, either once after a delay or repeatedly on
/// a schedule.
/// </summary>
/// <remarks>
/// A job is resolved from a fresh dependency injection scope for every execution, so it may take
/// scoped dependencies such as a DbContext through its constructor. Delivery is at-least-once: a
/// process that dies after the work is done but before the run is marked complete lets another
/// instance pick the run up again once the lease expires, so an implementation must be idempotent.
/// </remarks>
public interface IJob
{
    /// <summary>
    /// Runs the job.
    /// </summary>
    /// <param name="context">The execution context describing this run.</param>
    /// <param name="cancellationToken">
    /// A cancellation token that is signalled when the host shuts down or the execution timeout elapses.
    /// </param>
    /// <returns>A task that completes when the work is done.</returns>
    Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default);
}
