namespace Bsft.Messaging;

/// <summary>
/// One row of an outbox listing: everything about a message except the message.
/// </summary>
/// <remarks>
/// The payload is absent on purpose. What an operator needs to triage a parked message - which event,
/// when, how often it was tried and what the last failure said - is all here, and the payload is a
/// second grade of read behind its own permission. A listing that carried it would make that
/// permission decorative.
/// </remarks>
/// <param name="Id">The row's identifier, which the detail read is addressed by.</param>
/// <param name="EventId">The integration event's own identifier, as a consumer deduplicates on.</param>
/// <param name="EventTypeName">The event type, as the registry names it.</param>
/// <param name="OccurredAt">When the event was staged.</param>
/// <param name="CorrelationId">The request that staged it, where one was in scope.</param>
/// <param name="State">Where the row stands.</param>
/// <param name="AttemptCount">How many delivery attempts have been made.</param>
/// <param name="NextAttemptAt">When the next attempt is due, where one is.</param>
/// <param name="LeasedUntil">How long the current claim holds, where a dispatcher holds one.</param>
/// <param name="ProcessedAt">When it was published, where it was.</param>
/// <param name="DeadLetteredAt">When it was retired, where it was.</param>
/// <param name="FailureReason">What the last failure said, where there was one.</param>
public sealed record OutboxMessageSummary(
    long Id,
    Guid EventId,
    string EventTypeName,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    OutboxMessageState State,
    int AttemptCount,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? LeasedUntil,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? DeadLetteredAt,
    string? FailureReason);
