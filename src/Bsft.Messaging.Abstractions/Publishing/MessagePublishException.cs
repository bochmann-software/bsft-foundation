namespace Bsft.Messaging.Publishing;

/// <summary>
/// Thrown when publishing an integration event failed. The failure of the underlying transport or of
/// a subscriber is kept as the inner exception, so the outbox dispatcher can treat a publish failure
/// as one recoverable condition and leave the message pending for a later attempt.
/// </summary>
public sealed class MessagePublishException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessagePublishException"/> class.
    /// </summary>
    public MessagePublishException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagePublishException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    public MessagePublishException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagePublishException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The failure that caused the publish to fail.</param>
    public MessagePublishException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
