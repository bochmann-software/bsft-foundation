namespace Bsft.Scheduling;

/// <summary>
/// The set of jobs known to the scheduler, keyed by the name their runs are stored under.
/// </summary>
public interface IJobRegistry
{
    /// <summary>Gets every job definition, in registration order.</summary>
    IReadOnlyList<JobDefinition> Definitions { get; }

    /// <summary>
    /// Finds a job definition by name.
    /// </summary>
    /// <param name="name">The registered job name.</param>
    /// <returns>The definition, or <c>null</c> when no job of that name is registered.</returns>
    JobDefinition? Find(string name);

    /// <summary>
    /// Says whether a job is currently switched on, which is what decides whether the scheduler gives
    /// it occurrences and executes them.
    /// </summary>
    /// <param name="name">The registered job name.</param>
    /// <returns>
    /// <c>true</c> when the job runs in this deployment. An unregistered name is not enabled.
    /// </returns>
    /// <remarks>
    /// It answers from that job's <see cref="BsftJobOptions"/> every time it is asked rather than from
    /// something captured at registration, so the switch follows a configuration reload and a host
    /// never has to decide at startup whether to register the job at all.
    /// </remarks>
    bool IsEnabled(string name);

    /// <summary>
    /// Finds a job definition by the implementing type, which is how the generic enqueue overloads
    /// resolve a job without the caller repeating its name.
    /// </summary>
    /// <param name="jobType">The <see cref="IJob"/> implementation type.</param>
    /// <returns>The definition, or <c>null</c> when the type is not registered.</returns>
    JobDefinition? FindByType(Type jobType);
}
