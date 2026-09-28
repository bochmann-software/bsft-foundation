namespace Bsft.Scheduling.Heartbeats;

/// <summary>
/// Remembers when each job last completed a run, which is what "the job is still alive" is measured
/// against.
/// </summary>
/// <remarks>
/// <para>
/// The default is per instance and in memory. A job runs once across a deployment, so an instance that
/// never wins a claim never records anything and would report every job dead if this were a verdict.
/// It is not: the liveness check reports what <em>this</em> instance has seen since it started, and a
/// deployment reads the answer across its instances the way it reads any other per-instance signal.
/// </para>
/// <para>
/// Replaceable through the container, which is the seam a host uses to answer that question
/// differently — from the run store, or from a shared cache. The two methods are asynchronous for that
/// seam and for no other reason: the in-memory default has nothing to await, and an implementation
/// that reaches a database would otherwise have to block a settling run on synchronous I/O.
/// </para>
/// </remarks>
public interface IJobHeartbeatStore
{
    /// <summary>
    /// Gets the instant this store started observing, which is the baseline for a job that has not
    /// completed a run yet.
    /// </summary>
    /// <remarks>
    /// Without it a job that has never run would either be permanently healthy — nothing to be late
    /// against — or permanently overdue, and the first is how a job that never starts stays invisible.
    /// Measuring from the start of the process makes "it has not run since we came up" a statement
    /// with a clock on it.
    /// </remarks>
    DateTimeOffset ObservingSince { get; }

    /// <summary>
    /// Records that a job just completed a run successfully.
    /// </summary>
    /// <param name="jobName">The registered job name.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the heartbeat has been recorded.</returns>
    Task RecordCompletedAsync(string jobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns when the job last completed a run as this store sees it.
    /// </summary>
    /// <param name="jobName">The registered job name.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The instant of the last successful run, or <c>null</c> when there has been none.</returns>
    Task<DateTimeOffset?> LastCompletedAtAsync(string jobName, CancellationToken cancellationToken = default);
}
