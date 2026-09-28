namespace Bsft.Scheduling;

/// <summary>
/// One registered job: the name runs are stored under, the type that implements it and, for a
/// recurring job, how often it repeats.
/// </summary>
/// <param name="Name">
/// The name runs are stored under. It is persisted, so renaming a job orphans its pending runs.
/// </param>
/// <param name="JobType">The <see cref="IJob"/> implementation resolved for each execution.</param>
/// <param name="Schedule">
/// The recurrence rule, or <c>null</c>. A <c>null</c> schedule on a type implementing
/// <see cref="IRecurringJob"/> means the rule is read from the job instance at startup; on any other
/// type it means the job only ever runs when it is enqueued explicitly.
/// </param>
/// <param name="OverlapPolicy">Whether two executions of this job may overlap.</param>
public sealed record JobDefinition(
    string Name,
    Type JobType,
    JobSchedule? Schedule,
    JobOverlapPolicy OverlapPolicy)
{
    /// <summary>
    /// Gets a value indicating whether this job recurs, either because a schedule was given or
    /// because the job type declares one of its own.
    /// </summary>
    public bool IsRecurring => Schedule is not null || typeof(IRecurringJob).IsAssignableFrom(JobType);
}
