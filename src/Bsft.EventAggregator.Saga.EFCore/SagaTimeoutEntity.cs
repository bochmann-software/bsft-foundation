namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// The persisted form of a durable saga timeout.
/// </summary>
public sealed class SagaTimeoutEntity
{
    /// <summary>Gets or sets the primary key.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the saga type the timeout belongs to.</summary>
    public string SagaType { get; set; } = string.Empty;

    /// <summary>Gets or sets the correlation key of the saga instance.</summary>
    public string CorrelationKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the timeout name, unique per pending saga instance.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the waiting step index whose reminders this timeout belongs to.</summary>
    public int StepIndex { get; set; }

    /// <summary>
    /// Gets or sets the UTC instant at which the timeout fires. Stored as a <see cref="DateTime"/>
    /// rather than a <see cref="DateTimeOffset"/> because relational providers differ in whether they
    /// can order and compare offsets — SQLite cannot translate such a comparison at all.
    /// </summary>
    public DateTime DueAt { get; set; }

    /// <summary>Gets or sets the JSON-serialized event payload.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified type name of the event payload.</summary>
    public string PayloadTypeName { get; set; } = string.Empty;

    /// <summary>Gets or sets the correlation id captured when the timeout was scheduled.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Gets or sets the number of failed dispatch attempts.</summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Gets or sets the UTC instant the timeout left the pending set — dispatched, skipped by the
    /// catch-up policy or retired to the dead-letter state — or <c>null</c> while it is still pending.
    /// The pending-row index filters on this column, so every terminal state has to stamp it;
    /// <see cref="DeadLetteredAt"/> tells the three apart.
    /// </summary>
    public DateTime? DispatchedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC instant the timeout was retired after failing too often, or <c>null</c>
    /// when it was never retired. A retired row is kept and stays queryable, but it is never dispatched
    /// again.
    /// </summary>
    public DateTime? DeadLetteredAt { get; set; }

    /// <summary>
    /// Gets or sets the failure that retired the timeout, or <c>null</c> when it was never retired.
    /// </summary>
    /// <remarks>
    /// This is the message of the exception that failed the last dispatch, so it is operational data
    /// with the reach of a stored row rather than of a log line: an exception raised while resolving or
    /// publishing a timeout payload can quote parts of that payload. Treat the column as at least as
    /// sensitive as the payload, and keep personal data out of the exceptions a saga step throws.
    /// </remarks>
    public string? DeadLetterReason { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token guarding the dispatch claim.</summary>
    public Guid ConcurrencyToken { get; set; }
}
