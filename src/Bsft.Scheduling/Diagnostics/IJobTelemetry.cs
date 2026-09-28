namespace Bsft.Scheduling.Diagnostics;

/// <summary>
/// Emits traces and metrics for job scheduling: enqueue, claim and outcome counters plus a per-run
/// span carrying the execution duration.
/// </summary>
public interface IJobTelemetry
{
    /// <summary>Records that a run was enqueued.</summary>
    /// <param name="jobName">The registered job name.</param>
    void JobEnqueued(string jobName);

    /// <summary>Records that a run was claimed and is about to execute.</summary>
    /// <param name="jobName">The registered job name.</param>
    void JobClaimed(string jobName);

    /// <summary>Records that the catch-up policy discarded an overdue run.</summary>
    /// <param name="jobName">The registered job name.</param>
    void JobSkipped(string jobName);

    /// <summary>
    /// Begins a telemetry scope for a single execution. Dispose it when the run finishes to record
    /// the duration and the outcome.
    /// </summary>
    /// <param name="jobName">The registered job name.</param>
    /// <param name="jobId">The identity of the run, recorded as a span tag.</param>
    /// <param name="attemptNumber">The one-based attempt number, recorded as a span tag.</param>
    /// <returns>A scope that must be disposed when the run finishes.</returns>
    IJobExecutionTelemetryScope BeginExecution(string jobName, long jobId, int attemptNumber);
}
