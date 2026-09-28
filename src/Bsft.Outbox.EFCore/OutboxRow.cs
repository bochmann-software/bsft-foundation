namespace Bsft.Outbox.EFCore;

/// <summary>
/// The lifecycle columns every outbox row carries, as mapped properties an entity inherits rather
/// than declares.
/// </summary>
/// <remarks>
/// <para>
/// It is a base class rather than only the <see cref="IOutboxRow"/> interface because the lease
/// protocol builds its queries against these members. A predicate whose member is declared on an
/// interface is a predicate a provider has to resolve back to a mapped property by name; a member
/// declared on an unmapped base class is simply a property of the entity, which is what every EF Core
/// provider already translates.
/// </para>
/// <para>
/// It shares implementation and imposes no contract of its own, so it is a plain class: an outbox
/// entity derives from it and adds what its rows carry. It is not mapped itself, and an outbox is one
/// table per derived entity — that is the whole point of the type parameter on
/// <see cref="EFCoreOutboxStore{TEntity, TPayload}"/> — so nothing here belongs to a hierarchy
/// a provider has to discriminate between.
/// </para>
/// </remarks>
public class OutboxRow : IOutboxRow
{
    /// <summary>Gets or sets the auto-increment primary key, which also defines the drain order.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets when the row's side effect was applied, or <see langword="null"/> while pending.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC instant until which a drainer holds this row, or <see langword="null"/>
    /// while it is unclaimed. A lease that has passed is treated as unclaimed, which is what lets the
    /// rows of a drainer that died mid-pass be picked up again.
    /// </summary>
    public DateTime? LeasedUntil { get; set; }

    /// <summary>
    /// Gets or sets the token of the current lease, or <see langword="null"/> while the row is
    /// unclaimed. It is replaced on every claim, so only the drainer holding the current lease can
    /// complete the row.
    /// </summary>
    public Guid? LeaseToken { get; set; }

    /// <summary>Gets or sets the number of attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Gets or sets the earliest UTC instant at which the row may be leased again after a failed
    /// attempt, or <see langword="null"/> when nothing is holding it back.
    /// </summary>
    /// <remarks>
    /// It is what makes a retry delay a property of the row rather than of the drainer that failed it:
    /// the wait survives a restart, and every instance respects it because all of them read the same
    /// row. <see langword="null"/> is the honest value both for a row that has never failed and for
    /// one written before this column existed; both are eligible at once.
    /// </remarks>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>
    /// Gets or sets when the row was retired after failing too often, or <see langword="null"/> while
    /// it is still being attempted. A retired row is kept and stays queryable, but it is never leased
    /// again.
    /// </summary>
    public DateTime? DeadLetteredAt { get; set; }

    /// <summary>
    /// Gets or sets the failure that retired the row, or <see langword="null"/> while it has not been
    /// retired.
    /// </summary>
    /// <remarks>
    /// This is the message of the exception that ended the last attempt, so it is operational data
    /// with the reach of a stored row rather than of a log line: an exception raised while applying a
    /// side effect can quote parts of the payload. Treat the column as at least as sensitive as the
    /// payload, and do not put personal data into the exception messages a side effect throws.
    /// </remarks>
    public string? FailureReason { get; set; }
}
