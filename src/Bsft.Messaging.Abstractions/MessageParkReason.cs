
namespace Bsft.Messaging;

/// <summary>
/// Why a message was set aside instead of handled. It is a small closed set on purpose: it is recorded
/// as a metric tag, and a free-text reason there would give the metric one series per distinct failure.
/// </summary>
/// <remarks>
/// <para>
/// The free-text detail is not lost — it goes into the log entry and into the reason handed to
/// <see cref="Consuming.IMessageDelivery.ParkAsync"/>, where an operator reads it. This enum is only
/// the category the detail belongs to, and the reason a parked message carries is the category, a colon
/// and the detail.
/// </para>
/// <para>
/// It lives in the abstractions rather than in the consumer host because both sides of the transport
/// seam park messages. The host parks the ones it cannot handle; a transport parks the ones that never
/// become a message at all. A transport that had to invent its own category would put a token into the
/// same header that nothing could enumerate.
/// </para>
/// </remarks>
public enum MessageParkReason
{
    /// <summary>
    /// The message's event name is not registered in this process, so there is no type to decode it
    /// into. Either the producer published an event this consumer was not built for, or the name was
    /// left out of <c>BsftMessagingOptions.AddEvent</c>.
    /// </summary>
    UnknownEventName,

    /// <summary>
    /// The message was written under a version of the message contract that is newer than the one this
    /// consumer understands, so fields it may depend on cannot be read.
    /// </summary>
    UnsupportedMessageVersion,

    /// <summary>
    /// The event name is known but the payload did not decode into it — a truncated message, or a
    /// payload written under an incompatible shape of the same event name.
    /// </summary>
    UndecodablePayload,

    /// <summary>
    /// The handlers kept failing until the message ran out of delivery attempts. It is the only reason
    /// that follows a genuine attempt to handle the message rather than a refusal to try.
    /// </summary>
    AttemptsExhausted,

    /// <summary>
    /// The transport could not read the delivery as an <see cref="IntegrationEventMessage"/>
    /// at all, so the consumer host was never shown it. It is the one reason a transport rather than the
    /// host records, and the only one that is never retried: the same bytes would be exactly as
    /// unreadable on a redelivery.
    /// </summary>
    UnreadableEnvelope,
}
