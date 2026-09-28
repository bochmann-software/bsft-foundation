namespace Bsft.Outbox;

/// <summary>
/// The lifecycle of one durable outbox row: what a drain pass reads to decide whether the row may be
/// handed out, and what it writes to record how the attempt ended.
/// </summary>
/// <remarks>
/// <para>
/// It names no payload. What an outbox row carries is the business of whoever wrote it — an
/// integration event, a notification, an intent to apply later — and the lease protocol never reads
/// it, so a row contract that named it would force every outbox in a host onto one payload shape.
/// The payload rides as a type parameter instead, through
/// <see cref="IOutboxStore{TPayload}"/>.
/// </para>
/// <para>
/// Every member is settable because the store writes them, and every instant is a
/// <see cref="DateTime"/> in UTC rather than a <see cref="DateTimeOffset"/>: an offset that is always
/// zero costs a column on every provider and buys nothing, and the conversion happens once, at the
/// store boundary.
/// </para>
/// </remarks>
public interface IOutboxRow
{
    /// <summary>Gets or sets the store-assigned row identity, which also defines the drain order.</summary>
    long Id { get; set; }

    /// <summary>
    /// Gets or sets when the row's side effect was applied, or <see langword="null"/> while it is
    /// pending. A row that carries an instant here is never handed out again.
    /// </summary>
    DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC instant until which a drainer holds this row, or <see langword="null"/>
    /// while it is unclaimed. A lease that has passed counts as unclaimed, which is what lets the rows
    /// of a drainer that died mid-pass be picked up again.
    /// </summary>
    DateTime? LeasedUntil { get; set; }

    /// <summary>
    /// Gets or sets the token of the current lease, or <see langword="null"/> while the row is
    /// unclaimed. It is replaced on every claim, so only the holder of the current lease can complete
    /// the row.
    /// </summary>
    Guid? LeaseToken { get; set; }

    /// <summary>Gets or sets the number of attempts made so far.</summary>
    int AttemptCount { get; set; }

    /// <summary>
    /// Gets or sets the earliest UTC instant at which the row may be leased again after a failed
    /// attempt, or <see langword="null"/> when nothing is holding it back.
    /// </summary>
    /// <remarks>
    /// It is what makes a retry delay a property of the row rather than of the drainer that failed it:
    /// the wait survives a restart, and every instance respects it because all of them read the same
    /// row.
    /// </remarks>
    DateTime? NextAttemptAt { get; set; }

    /// <summary>
    /// Gets or sets when the row was retired after failing too often, or <see langword="null"/> while
    /// it is still being attempted. A retired row is kept and stays queryable, but it is never leased
    /// again.
    /// </summary>
    DateTime? DeadLetteredAt { get; set; }

    /// <summary>
    /// Gets or sets the failure that retired the row, or <see langword="null"/> while it has not been
    /// retired.
    /// </summary>
    /// <remarks>
    /// It is the message of the exception that ended the last attempt, so it is operational data with
    /// the reach of a stored row rather than of a log line: an exception raised while applying a side
    /// effect can quote parts of the payload. Treat the column as at least as sensitive as the payload
    /// itself.
    /// </remarks>
    string? FailureReason { get; set; }
}
