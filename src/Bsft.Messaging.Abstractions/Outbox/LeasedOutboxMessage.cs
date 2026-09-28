namespace Bsft.Messaging.Outbox;

/// <summary>
/// An outbox message that a dispatcher has claimed for delivery, together with the lease token that
/// proves the claim. Only the holder of the token may mark the message processed or abandon it, which
/// is what keeps two application instances from both completing the same message.
/// </summary>
public sealed record LeasedOutboxMessage
{
    /// <summary>Gets the store-assigned row identity of the message.</summary>
    public long Id { get; init; }

    /// <summary>Gets the identity of the integration event carried by the message.</summary>
    public Guid EventId { get; init; }

    /// <summary>Gets the registered type name of the integration event.</summary>
    public string EventTypeName { get; init; } = string.Empty;

    /// <summary>Gets the integration event serialized as JSON.</summary>
    public string PayloadJson { get; init; } = string.Empty;

    /// <summary>Gets the UTC instant at which the event occurred.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Gets the correlation id of the request that staged the event, or <see langword="null"/> when
    /// none was ambient at that moment — which is also what a row written before the store recorded
    /// correlation ids reads back as.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>Gets the number of delivery attempts made before this one.</summary>
    public int AttemptCount { get; init; }

    /// <summary>Gets the token that proves this dispatcher holds the lease on the message.</summary>
    public Guid LeaseToken { get; init; }
}
