namespace Bsft.Scheduling;

/// <summary>
/// One scheduled execution of a job as the store holds it. A recurring job is represented by exactly
/// one pending run at a time; the next one is created when the current one settles.
/// </summary>
public sealed class JobRun
{
    /// <summary>Gets or sets the store-assigned identity of the run.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the registered name of the job to execute.</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>Gets or sets the instant at which the run becomes due.</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>Gets or sets the lifecycle state of the run.</summary>
    public JobRunStatus Status { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the run belongs to a recurring registration. Only one
    /// pending run may exist per recurring job, which is what keeps a restart from seeding a second.
    /// </summary>
    public bool IsRecurring { get; set; }

    /// <summary>Gets or sets the opaque payload handed to the job, or <c>null</c> when there is none.</summary>
    public string? Payload { get; set; }

    /// <summary>Gets or sets the correlation id captured when the run was enqueued.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Gets or sets the number of attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Gets or sets the identifier of the instance holding the lease, or <c>null</c> when unclaimed.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>
    /// Gets or sets the instant at which the current lease expires. Once it has passed the run is
    /// claimable again, which is how the work of a crashed instance is recovered.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>Gets or sets the instant at which the run reached a terminal state.</summary>
    public DateTimeOffset? SettledAt { get; set; }

    /// <summary>
    /// Gets or sets the optimistic concurrency token that turns a claim race between two instances
    /// into a detectable conflict instead of a double execution.
    /// </summary>
    public Guid ConcurrencyToken { get; set; }
}
