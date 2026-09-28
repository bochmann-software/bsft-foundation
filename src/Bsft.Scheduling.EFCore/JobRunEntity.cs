namespace Bsft.Scheduling.EFCore;

/// <summary>
/// The persisted form of a <see cref="JobRun"/>.
/// </summary>
/// <remarks>
/// Instants are stored as <see cref="DateTime"/> in UTC rather than <see cref="DateTimeOffset"/>:
/// the offset is always zero and several providers store a <see cref="DateTimeOffset"/> in a way that
/// cannot be compared or indexed as cheaply.
/// </remarks>
public sealed class JobRunEntity
{
    /// <summary>Gets or sets the identity of the run.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the registered name of the job to execute.</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC instant at which the run becomes due.</summary>
    public DateTime DueAt { get; set; }

    /// <summary>Gets or sets the lifecycle state of the run.</summary>
    public JobRunStatus Status { get; set; }

    /// <summary>Gets or sets a value indicating whether the run belongs to a recurring registration.</summary>
    public bool IsRecurring { get; set; }

    /// <summary>Gets or sets the opaque payload handed to the job.</summary>
    public string? Payload { get; set; }

    /// <summary>Gets or sets the correlation id captured when the run was enqueued.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Gets or sets the number of attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Gets or sets the identifier of the instance holding the lease.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>Gets or sets the UTC instant at which the current lease expires.</summary>
    public DateTime? LeaseExpiresAt { get; set; }

    /// <summary>Gets or sets the UTC instant at which the run reached a terminal state.</summary>
    public DateTime? SettledAt { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token guarding the claim.</summary>
    public Guid ConcurrencyToken { get; set; }
}
