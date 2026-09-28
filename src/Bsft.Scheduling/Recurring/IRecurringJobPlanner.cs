namespace Bsft.Scheduling.Recurring;

/// <summary>
/// Creates the pending run that represents the next occurrence of a recurring job.
/// </summary>
/// <remarks>
/// A recurring job is never a repeating timer in memory. It is one pending row at a time, and the
/// following occurrence is created when the current one settles. That is what makes the schedule
/// survive a restart and what stops several instances from each running their own copy.
/// </remarks>
public interface IRecurringJobPlanner
{
    /// <summary>
    /// Creates the next occurrence of a recurring job, unless one is already live. A job that is not
    /// registered, or that does not recur, is a no-op.
    /// </summary>
    /// <param name="jobName">The registered job name.</param>
    /// <param name="previousDueAt">
    /// The due instant of the occurrence that just finished, or the seeding instant at startup. The
    /// next occurrence is computed from whichever of it and the current time is later, so a job that
    /// fell behind returns to its grid instead of replaying every missed tick.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns><c>true</c> when an occurrence was created.</returns>
    Task<bool> TryScheduleNextAsync(string jobName, DateTimeOffset previousDueAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the first occurrence of every recurring job that does not have one yet. Runs on
    /// startup and is safe to run on every instance.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when every recurring job has an occurrence.</returns>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
