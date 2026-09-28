namespace Bsft.Scheduling.Heartbeats;

/// <summary>
/// What the liveness check found out about one recurring job: how long it has been silent, how long it
/// is allowed to be, and where that allowance came from.
/// </summary>
/// <param name="JobName">The registered job name.</param>
/// <param name="LastCompletedAt">
/// When the job last completed a run on this instance, or <c>null</c> when it has completed none since
/// the instance started.
/// </param>
/// <param name="MeasuredFrom">
/// The instant the silence is measured from: the last completed run, or the instant this instance
/// started observing where there has been none.
/// </param>
/// <param name="Silence">How long the job has been silent.</param>
/// <param name="Tolerance">How long it is allowed to be silent.</param>
/// <param name="ExpectedInterval">
/// The gap between two occurrences of the job's schedule, or <c>null</c> where the tolerance was
/// configured outright and no gap was derived.
/// </param>
/// <remarks>
/// One of these per watched job is put on the health-check result's data, so an operator reading the
/// endpoint sees which job is late and by how much rather than a single failed check with a count in
/// it.
/// </remarks>
public sealed record JobHeartbeatSnapshot(
    string JobName,
    DateTimeOffset? LastCompletedAt,
    DateTimeOffset MeasuredFrom,
    TimeSpan Silence,
    TimeSpan Tolerance,
    TimeSpan? ExpectedInterval)
{
    /// <summary>Gets a value indicating whether the job has been silent longer than it is allowed to be.</summary>
    public bool IsOverdue => Silence > Tolerance;
}
