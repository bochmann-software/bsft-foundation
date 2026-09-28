namespace Bsft.Messaging;

/// <summary>
/// What became of a message that the consumer host was able to decode: it was handled, or the inbox
/// recognised it as one this consumer had already finished with.
/// </summary>
/// <remarks>
/// Both outcomes end in the message being acknowledged, so this is not a success-or-failure
/// distinction. It exists because the two are worth telling apart when reading metrics: a steady
/// trickle of duplicates is at-least-once delivery working as designed, while a sudden flood of them
/// usually means a consumer that acknowledges too late or a producer republishing its backlog.
/// </remarks>
public enum EventDeliveryOutcome
{
    /// <summary>The event was new to this consumer, and every registered handler ran and succeeded.</summary>
    Handled,

    /// <summary>
    /// The inbox already held a completed delivery of this event for this consumer, so the message was
    /// a redelivery and no handler was invoked.
    /// </summary>
    Duplicate,
}
