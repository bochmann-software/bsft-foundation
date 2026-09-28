namespace Bsft.Scheduling;

/// <summary>
/// Persistence for scheduled job runs, including the lease that lets several application instances
/// share one schedule without ever running the same occurrence twice.
/// </summary>
/// <remarks>
/// <para>
/// The claim is the only part of the contract that has to be atomic. <see cref="ClaimAsync"/>
/// must move a run from <see cref="JobRunStatus.Pending"/> to <see cref="JobRunStatus.Running"/> in a
/// single conditional operation guarded by <see cref="JobRun.ConcurrencyToken"/>, so that exactly one
/// caller can win. Everything else may be implemented as ordinary reads and writes.
/// </para>
/// <para>
/// Execution is at-least-once, not exactly-once: an instance that dies mid-run leaves the lease to
/// expire, and the next poll re-claims the run. A job implementation must be idempotent.
/// </para>
/// </remarks>
public interface IJobStore
{
    /// <summary>
    /// Persists a new run and returns its assigned identity.
    /// </summary>
    /// <param name="run">The run to store.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The identity assigned to the run.</returns>
    Task<long> EnqueueAsync(JobRun run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a recurring run unless the job already has a live occurrence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A job never has more than one <em>pending</em> occurrence, whatever the policy: that is what
    /// makes seeding idempotent, since every instance seeds the same recurring jobs at startup and
    /// all but the first are expected to find the occurrence already there. An implementation must
    /// enforce it atomically, not merely check for it.
    /// </para>
    /// <para>
    /// Under <see cref="JobOverlapPolicy.Forbid"/> an occurrence that is currently <em>running</em>
    /// counts as live too, so the successor cannot be created while the job is still working. Under
    /// <see cref="JobOverlapPolicy.Allow"/> it does not, which is exactly what lets the job keep its
    /// cadence across a slow run.
    /// </para>
    /// </remarks>
    /// <param name="run">The run to store.</param>
    /// <param name="overlapPolicy">Whether a currently running occurrence blocks the new one.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns><c>true</c> when the run was stored; <c>false</c> when a live occurrence already existed.</returns>
    Task<bool> TryEnqueueRecurringAsync(JobRun run, JobOverlapPolicy overlapPolicy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the runs that are ready to execute: pending runs that are due, plus running runs whose
    /// lease has expired because the instance that held it went away.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <param name="batchSize">The maximum number of runs to return.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The claimable runs, oldest due first.</returns>
    Task<IReadOnlyList<JobRun>> ListClaimableAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to take the lease on a run, marking it running and incrementing its attempt count.
    /// </summary>
    /// <param name="run">The run as read by <see cref="ListClaimableAsync"/>, carrying the token the claim is checked against.</param>
    /// <param name="owner">An identifier for the claiming instance, recorded for diagnostics.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="leaseExpiresAt">The instant at which the lease lapses and the run becomes claimable again.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// The claimed run carrying its new concurrency token, or <c>null</c> when another instance won
    /// the race or the run was cancelled in the meantime.
    /// </returns>
    Task<JobRun?> ClaimAsync(JobRun run, string owner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a claimed run to a terminal state and releases its lease.
    /// </summary>
    /// <param name="run">The claimed run, carrying the token from <see cref="ClaimAsync"/>.</param>
    /// <param name="status">The terminal state to record.</param>
    /// <param name="settledAt">The instant to record as the settlement time.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task SettleAsync(JobRun run, JobRunStatus status, DateTimeOffset settledAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a claimed run to <see cref="JobRunStatus.Pending"/> so it is attempted again later.
    /// The attempt count is kept, which is what eventually exhausts the retries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A retry of a recurring run gives up the single pending-occurrence slot when, and only when,
    /// another pending occurrence of the same job already holds it. That is the
    /// <see cref="JobOverlapPolicy.Allow"/> case: the successor went out before the work started, so
    /// the retry is a leftover attempt at a past occurrence rather than the schedule's next one, and
    /// the cadence is already carried by the successor. Under <see cref="JobOverlapPolicy.Forbid"/> no
    /// successor exists yet, the retry genuinely is the job's pending occurrence, and it keeps the slot
    /// so that no duplicate can be created beside it.
    /// </para>
    /// <para>
    /// The retry itself is unaffected either way: same attempt count, same due instant, same route to
    /// <see cref="JobRunStatus.Failed"/> once the attempts are used up.
    /// </para>
    /// </remarks>
    /// <param name="run">The claimed run, carrying the token from <see cref="ClaimAsync"/>.</param>
    /// <param name="dueAt">The instant at which the retry becomes due.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task RetryAsync(JobRun run, DateTimeOffset dueAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a run that has not started yet.
    /// </summary>
    /// <param name="id">The identity of the run to cancel.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns><c>true</c> when a pending run was removed; <c>false</c> when it was already running or gone.</returns>
    Task<bool> CancelAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single run by its identity.
    /// </summary>
    /// <param name="id">The identity of the run.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The run, or <c>null</c> when no run with that identity exists.</returns>
    Task<JobRun?> FindAsync(long id, CancellationToken cancellationToken = default);
}
