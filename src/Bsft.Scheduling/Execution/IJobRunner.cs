namespace Bsft.Scheduling.Execution;

/// <summary>
/// Takes a claimed run from start to settlement: applies the catch-up policy, executes it, records
/// the outcome or schedules a retry, and creates the next occurrence of a recurring job.
/// </summary>
public interface IJobRunner
{
    /// <summary>
    /// Runs one claimed job run to completion. Does not throw for a failing job — a failure is
    /// recorded on the run instead, so one bad job cannot stop the dispatcher.
    /// </summary>
    /// <param name="claimed">The run, already claimed and carrying the lease token.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the run has settled.</returns>
    Task RunAsync(JobRun claimed, CancellationToken cancellationToken = default);
}
