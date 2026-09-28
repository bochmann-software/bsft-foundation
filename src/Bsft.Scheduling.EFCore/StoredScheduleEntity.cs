namespace Bsft.Scheduling.EFCore;

/// <summary>
/// The persisted form of a <see cref="StoredSchedules.StoredSchedule"/>.
/// </summary>
/// <remarks>
/// Instants are stored as <see cref="DateTime"/> in UTC rather than <see cref="DateTimeOffset"/>, for
/// the same reason as <see cref="JobRunEntity"/>. The recurrence rule is split into
/// <see cref="CronExpression"/> and <see cref="IntervalTicks"/> rather than kept as a single opaque
/// string, so that <see cref="StoredScheduleEntityConfiguration"/> can constrain exactly
/// one of the two to be set and a row can never persist a schedule
/// <see cref="Bsft.Scheduling.JobSchedule"/> itself would refuse to construct.
/// </remarks>
public sealed class StoredScheduleEntity
{
    /// <summary>Gets or sets the identity of the schedule.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the tenant this schedule belongs to, or <c>null</c> when it belongs to none.</summary>
    public string? TenantId { get; set; }

    /// <summary>Gets or sets the registered job name this schedule enqueues.</summary>
    public string JobKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the cron expression, or <c>null</c> when this is an interval schedule.</summary>
    public string? CronExpression { get; set; }

    /// <summary>Gets or sets the interval in ticks, or <c>null</c> when this is a cron schedule.</summary>
    public long? IntervalTicks { get; set; }

    /// <summary>Gets or sets the opaque payload handed to the job on every occurrence.</summary>
    public string? Payload { get; set; }

    /// <summary>Gets or sets a value indicating whether the schedule is switched on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the UTC instant at which the next occurrence becomes due.</summary>
    public DateTime? NextRunAt { get; set; }

    /// <summary>Gets or sets the UTC instant at which the schedule last produced a run.</summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>Gets or sets the UTC instant at which the schedule was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets the UTC instant at which the schedule was last edited.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token guarding the advance.</summary>
    public Guid ConcurrencyToken { get; set; }
}
