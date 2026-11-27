namespace Bsft.Messaging.EFCore.Inbox;

/// <summary>
/// A record that a consumer has finished handling an integration event. Its existence is what makes a
/// redelivery of that event a no-op, so this table is the consuming side's idempotency.
/// </summary>
/// <remarks>
/// The row is written in the same transaction as whatever the handlers did, so the two cannot
/// disagree: there is no state in which the work happened and the record of it did not, or the other
/// way round. Rows are never updated — a delivery either produces one or does not.
/// </remarks>
public sealed class InboxMessageEntity
{
    /// <summary>
    /// The primary key: auto-increment, or, under <c>UseTenantKeys</c>, a time-ordered id the client
    /// creates, keyed together with <see cref="TenantId"/>.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The tenant the row belongs to. Mapped only under <c>UseTenantKeys</c>, where it is required and
    /// leads the key; it is then filled from the scope that writes the row. Without tenant keys the
    /// table has no such column.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>The identity of the integration event, stable across every redelivery of it.</summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// The consumer that processed the event. It is the other half of the identity of a delivery:
    /// two consumers of the same event are independent, and may share this table.
    /// </summary>
    public string Consumer { get; set; } = string.Empty;

    /// <summary>
    /// The registered name the event travelled under. Nothing reads it to make a decision; it is here
    /// so the table can be read by a human wondering what a given consumer has been doing.
    /// </summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>The UTC instant at which the message was received.</summary>
    public DateTime ReceivedAt { get; set; }
}
