namespace Bsft.Messaging;

/// <summary>
/// Narrows an outbox listing. Every member is optional and they combine as a conjunction.
/// </summary>
/// <remarks>
/// A typed filter rather than the generic <c>FilterNode</c> the paged request also carries: the
/// outbox table has eleven columns and one of them is the payload, so an expression language over it
/// would mostly be a way to write a predicate against the payload, which is exactly the read this
/// surface grades apart.
/// </remarks>
/// <param name="State">The state to restrict to.</param>
/// <param name="EventTypeName">The exact event type to restrict to.</param>
/// <param name="EventTypeNamePrefix">An event-type prefix, for a whole family of events.</param>
/// <param name="EventId">The event identifier to restrict to.</param>
/// <param name="CorrelationId">The correlating request to restrict to.</param>
/// <param name="OccurredAfter">The earliest occurrence to include.</param>
/// <param name="OccurredBefore">The latest occurrence to include.</param>
/// <param name="MinAttemptCount">
/// The fewest delivery attempts an included row must have made, for "show me what is struggling"
/// without waiting for it to be retired.
/// </param>
public sealed record OutboxMessageFilter(
    OutboxMessageState? State = null,
    string? EventTypeName = null,
    string? EventTypeNamePrefix = null,
    Guid? EventId = null,
    string? CorrelationId = null,
    DateTimeOffset? OccurredAfter = null,
    DateTimeOffset? OccurredBefore = null,
    int? MinAttemptCount = null);
