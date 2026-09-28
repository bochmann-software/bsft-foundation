namespace Bsft.Messaging;

/// <summary>
/// Thrown when a received message cannot be turned into an event this process can handle. It carries
/// the <see cref="MessageParkReason"/> the consumer host parks the message under, so the decision of
/// what kind of failure this is stays with the component that discovered it.
/// </summary>
/// <remarks>
/// Every failure it reports is permanent: an unregistered event name, a message contract from the
/// future and a payload that does not parse are all properties of the message rather than of the
/// moment, so retrying delivers the identical bytes to the identical code. That is why the host parks
/// on it immediately instead of spending the message's delivery attempts on it.
/// </remarks>
public sealed class MessageDecodeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageDecodeException"/> class.
    /// </summary>
    public MessageDecodeException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageDecodeException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    public MessageDecodeException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageDecodeException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The failure that prevented the message from being decoded.</param>
    public MessageDecodeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageDecodeException"/> class.
    /// </summary>
    /// <param name="parkReason">The category the message is parked under.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The failure that prevented the message from being decoded, if any.</param>
    public MessageDecodeException(MessageParkReason parkReason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ParkReason = parkReason;
    }

    /// <summary>
    /// Gets the category the consumer host parks the message under. Defaults to
    /// <see cref="MessageParkReason.UndecodablePayload"/>, which is what an instance created through
    /// one of the standard exception constructors reports.
    /// </summary>
    public MessageParkReason ParkReason { get; } = MessageParkReason.UndecodablePayload;
}
