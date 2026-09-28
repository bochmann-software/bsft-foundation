namespace Bsft.Scheduling.Reading;

/// <summary>
/// One job run as a listing shows it: which job, where it stands and when.
/// </summary>
/// <remarks>
/// It carries no payload. The arguments a run was enqueued with are whatever the enqueuing feature put
/// in them, so they are graded apart per section 5a of the package conventions and belong to
/// <see cref="JobRunView"/>.
/// <para>
/// There is no failure description here because the table stores none: a run that failed is recorded
/// as failed, and why it failed is in the logs and the telemetry of the instance that ran it.
/// </para>
/// </remarks>
/// <param name="Id">The run's identity.</param>
/// <param name="JobName">The job the run is an occurrence of.</param>
/// <param name="DueAt">When the run was due.</param>
/// <param name="Status">Where the run stands.</param>
/// <param name="IsRecurring">Whether the run came from a schedule rather than a one-off enqueue.</param>
/// <param name="CorrelationId">The correlation the run carries, or null when it carries none.</param>
/// <param name="AttemptCount">How many attempts have been made.</param>
/// <param name="LeaseOwner">The instance holding the run, or null when nobody does.</param>
/// <param name="LeaseExpiresAt">When the lease lapses, or null when there is none.</param>
/// <param name="SettledAt">When the run reached a terminal state, or null while it has not.</param>
public sealed record JobRunSummary(
    long Id,
    string JobName,
    DateTimeOffset DueAt,
    JobRunStatus Status,
    bool IsRecurring,
    string? CorrelationId,
    int AttemptCount,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? SettledAt);
