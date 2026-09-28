namespace Bsft.Messaging;

/// <summary>
/// Narrows an inbox listing. Every member is optional and they combine as a conjunction.
/// </summary>
/// <param name="Consumer">The consumer to restrict to, which is what the table is indexed by.</param>
/// <param name="EventName">The event type to restrict to.</param>
/// <param name="EventId">The event identifier to restrict to, for "did this one arrive".</param>
/// <param name="ReceivedAfter">The earliest arrival to include.</param>
/// <param name="ReceivedBefore">The latest arrival to include.</param>
public sealed record InboxMessageFilter(
    string? Consumer = null,
    string? EventName = null,
    Guid? EventId = null,
    DateTimeOffset? ReceivedAfter = null,
    DateTimeOffset? ReceivedBefore = null);
