namespace Bsft.Scheduling;

/// <summary>
/// The lifecycle state of a single job run.
/// </summary>
public enum JobRunStatus
{
    /// <summary>The run is waiting for its due time and may be claimed by any instance.</summary>
    Pending = 0,

    /// <summary>An instance holds a lease on the run and is executing it.</summary>
    Running = 1,

    /// <summary>The run finished successfully.</summary>
    Completed = 2,

    /// <summary>The run exhausted its retries and will not be attempted again.</summary>
    Failed = 3,

    /// <summary>The catch-up policy discarded the run without executing it.</summary>
    Skipped = 4,
}
