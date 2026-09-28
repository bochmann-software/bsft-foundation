namespace Bsft.Scheduling.Reading;

/// <summary>
/// Which job runs a listing narrows to. Every criterion that is set narrows the result; a filter with
/// none set lists the whole table.
/// </summary>
/// <remarks>
/// There is no tenant criterion because the run table carries no tenant column - runs are written
/// inside whichever database the host's own context is bound to, so the database is the scope.
/// </remarks>
/// <param name="JobName">The exact job to list the runs of, or null for every job.</param>
/// <param name="JobNamePrefix">A leading fragment of the job name, or null to match every name.</param>
/// <param name="Status">Where the run stands, or null for every status.</param>
/// <param name="IsRecurring">Whether to list recurring occurrences or one-off runs, or null for both.</param>
/// <param name="CorrelationId">The correlation the run carries, or null for every one.</param>
/// <param name="LeaseOwner">The instance holding the run, or null for every one.</param>
/// <param name="MinAttemptCount">The fewest attempts a run must have had, or null for no floor.</param>
/// <param name="DueAfter">The earliest moment a run may have been due, or null for no bound.</param>
/// <param name="DueBefore">The latest moment a run may have been due, or null for no bound.</param>
/// <param name="SettledAfter">The earliest settlement to include, or null for no bound.</param>
/// <param name="SettledBefore">The latest settlement to include, or null for no bound.</param>
public sealed record JobRunFilter(
    string? JobName = null,
    string? JobNamePrefix = null,
    JobRunStatus? Status = null,
    bool? IsRecurring = null,
    string? CorrelationId = null,
    string? LeaseOwner = null,
    int? MinAttemptCount = null,
    DateTimeOffset? DueAfter = null,
    DateTimeOffset? DueBefore = null,
    DateTimeOffset? SettledAfter = null,
    DateTimeOffset? SettledBefore = null);
