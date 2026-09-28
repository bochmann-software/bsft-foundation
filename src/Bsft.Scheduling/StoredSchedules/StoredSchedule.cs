namespace Bsft.Scheduling.StoredSchedules;

/// <summary>
/// A recurring schedule defined at runtime rather than in code: a tenant, a registered job key, a
/// recurrence rule and an opaque payload, editable through <see cref="IStoredScheduleService"/> without
/// a redeploy.
/// </summary>
/// <remarks>
/// A stored schedule never writes a <see cref="JobRun"/> of its own accord. It is turned into an
/// ordinary one-shot run — the same shape <see cref="IJobScheduler.EnqueueAtAsync"/> produces — the
/// instant it becomes due, so the dispatcher, the executor and the outcome policy need no awareness of
/// where a run came from. <see cref="NextRunAt"/> is what stands in for the single-occurrence slot a
/// code-registered recurring job gets from the unique index on <see cref="JobRun.JobName"/>; it is
/// advanced atomically through <see cref="ConcurrencyToken"/> so that two dispatcher instances racing
/// the same due slot produce exactly one run.
/// </remarks>
public sealed class StoredSchedule
{
    /// <summary>Gets or sets the store-assigned identity of the schedule.</summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the tenant this schedule belongs to, or <c>null</c> when it belongs to none.
    /// </summary>
    /// <remarks>
    /// A tenantless schedule runs like any other; the seeder does not read tenancy. What <c>null</c>
    /// does not mean is a schedule every tenant may reach: <see cref="IStoredScheduleService"/> holds
    /// a tenantless schedule to a tenantless caller, the same way it holds every other one to its owner.
    /// </remarks>
    public string? TenantId { get; set; }

    /// <summary>Gets or sets the registered job name this schedule enqueues.</summary>
    public string JobKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the recurrence rule.</summary>
    public JobSchedule Schedule { get; set; } = JobSchedule.Every(TimeSpan.FromMinutes(1));

    /// <summary>Gets or sets the opaque payload handed to the job on every occurrence, or <c>null</c>.</summary>
    public string? Payload { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the schedule is switched on. A disabled schedule is
    /// never read by <see cref="IStoredScheduleStore.ListDueAsync"/>, so it produces no runs until it is
    /// enabled again.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the instant at which this schedule's next occurrence becomes due, or <c>null</c>
    /// when the recurrence rule has no further occurrence.
    /// </summary>
    public DateTimeOffset? NextRunAt { get; set; }

    /// <summary>Gets or sets the instant at which the schedule last produced a run, or <c>null</c>.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>Gets or sets the instant at which the schedule was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the instant at which the schedule was last edited.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the optimistic concurrency token that turns a race between two dispatcher instances
    /// advancing the same due slot into a detectable conflict instead of a duplicate run.
    /// </summary>
    public Guid ConcurrencyToken { get; set; }
}
