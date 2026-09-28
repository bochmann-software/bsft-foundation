namespace Bsft.Scheduling.Execution;

/// <summary>
/// Decides and records what becomes of a claimed run: whether it is still worth executing, and how
/// its success, failure or discard is written back to the store.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="IJobRunner"/> so that the runner is only concerned with the order of
/// the steps, while every rule that depends on <see cref="BsftSchedulingOptions"/> — the retry budget,
/// the backoff, the catch-up window — lives in one place.
/// </remarks>
public interface IJobRunOutcomePolicy
{
    /// <summary>
    /// Determines whether a run became due so long ago that the configured catch-up policy discards
    /// it instead of executing it.
    /// </summary>
    /// <param name="run">The claimed run.</param>
    /// <returns><c>true</c> when the run should be skipped.</returns>
    bool IsBeyondCatchUpWindow(JobRun run);

    /// <summary>Records a run that finished successfully.</summary>
    /// <param name="run">The claimed run.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task RecordCompletedAsync(JobRun run, CancellationToken cancellationToken = default);

    /// <summary>Records a run the catch-up policy discarded without executing it.</summary>
    /// <param name="run">The claimed run.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task RecordSkippedAsync(JobRun run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed attempt: schedules a retry while the attempt budget lasts, and marks the run
    /// failed once it is spent.
    /// </summary>
    /// <param name="run">The claimed run.</param>
    /// <param name="failure">The exception the job produced.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task RecordFailureAsync(JobRun run, Exception failure, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an occurrence of a job that has since been switched off. It is settled as skipped
    /// rather than executed, and no successor is planned for it.
    /// </summary>
    /// <param name="run">The claimed run.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <remarks>
    /// A job is disabled while it may already have a pending occurrence — the one the planner wrote
    /// before the switch was flipped, or one another instance wrote at the same moment. Leaving it
    /// pending would make every poll pick it up again for as long as the job stays off.
    /// </remarks>
    Task RecordDisabledAsync(JobRun run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a run whose job is no longer registered. Leaving it pending would make every poll
    /// pick it up again for the life of the deployment.
    /// </summary>
    /// <param name="run">The claimed run.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task RecordUnregisteredAsync(JobRun run, CancellationToken cancellationToken = default);
}
