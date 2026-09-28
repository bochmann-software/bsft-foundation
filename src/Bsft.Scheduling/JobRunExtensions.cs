namespace Bsft.Scheduling;

/// <summary>
/// Internal helpers shared by the job store implementations.
/// </summary>
internal static class JobRunExtensions
{
    /// <summary>
    /// Creates a detached copy of a run.
    /// </summary>
    /// <remarks>
    /// Both stores hand out copies rather than the instances they hold, which is what a
    /// database-backed store necessarily does anyway. Sharing the copy here keeps the two from
    /// drifting apart when a field is added to <see cref="JobRun"/> — a field forgotten in one of two
    /// hand-written copies would silently disappear on the way out of that store.
    /// </remarks>
    /// <param name="run">The run to copy.</param>
    /// <returns>A new instance carrying the same values.</returns>
    internal static JobRun Copy(this JobRun run) => new()
    {
        Id = run.Id,
        JobName = run.JobName,
        DueAt = run.DueAt,
        Status = run.Status,
        IsRecurring = run.IsRecurring,
        Payload = run.Payload,
        CorrelationId = run.CorrelationId,
        AttemptCount = run.AttemptCount,
        LeaseOwner = run.LeaseOwner,
        LeaseExpiresAt = run.LeaseExpiresAt,
        SettledAt = run.SettledAt,
        ConcurrencyToken = run.ConcurrencyToken,
    };
}
