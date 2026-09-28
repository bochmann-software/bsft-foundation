namespace Bsft.Messaging;

/// <summary>
/// One row of an inbox listing: that a consumer has already handled an event, and when.
/// </summary>
/// <remarks>
/// There is no payload here and no second read behind it, because the inbox holds none. The table is
/// a deduplication ledger - four columns saying "this consumer has seen this event" - so the family's
/// inbound half keeps the single read permission that
/// <c>docs/package-conventions.md</c> section 5a prescribes for a family holding no content.
/// </remarks>
/// <param name="Id">The row's identifier.</param>
/// <param name="EventId">The integration event that was handled.</param>
/// <param name="Consumer">The consumer that handled it.</param>
/// <param name="EventName">The event type, as it arrived.</param>
/// <param name="ReceivedAt">When the delivery was recorded.</param>
public sealed record InboxMessageSummary(
    long Id, Guid EventId, string Consumer, string EventName, DateTimeOffset ReceivedAt);
