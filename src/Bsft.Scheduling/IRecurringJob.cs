namespace Bsft.Scheduling;

/// <summary>
/// A job that carries its own recurrence rule, so registration does not have to repeat it.
/// </summary>
/// <remarks>
/// The schedule is read once during startup from an instance resolved out of a temporary scope. It
/// must therefore be a constant of the job type rather than something derived from per-run state —
/// changing it at runtime has no effect until the host restarts. Register a job whose schedule comes
/// from configuration with the overload that takes the schedule explicitly instead.
/// </remarks>
public interface IRecurringJob : IJob
{
    /// <summary>Gets the recurrence rule of this job.</summary>
    JobSchedule Schedule { get; }
}
