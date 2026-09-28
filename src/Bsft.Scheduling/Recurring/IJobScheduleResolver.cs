namespace Bsft.Scheduling.Recurring;

/// <summary>
/// Answers what the recurrence rule of a registered job is, whichever of the two places it was stated
/// in.
/// </summary>
/// <remarks>
/// A job's schedule comes either from its registration — where a host passed one, typically because it
/// read it from configuration — or from the job type itself through
/// <see cref="IRecurringJob.Schedule"/>, which needs an instance and therefore a scope. Two callers
/// need the answer: the planner, to place the next occurrence, and the liveness check, to know how
/// long silence is normal for. Reading it in both places would mean resolving the job twice and
/// caching it twice.
/// </remarks>
public interface IJobScheduleResolver
{
    /// <summary>
    /// Returns the recurrence rule of a recurring job.
    /// </summary>
    /// <param name="jobName">The registered job name.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobName"/> is null.</exception>
    /// <exception cref="JobNotRegisteredException">Thrown when no job of that name is registered.</exception>
    /// <exception cref="InvalidCastException">
    /// Thrown when the registration carries no schedule and the job type does not implement
    /// <see cref="IRecurringJob"/> — which is to say the job does not recur at all.
    /// </exception>
    JobSchedule Resolve(string jobName);
}
