namespace Bsft.Messaging.RabbitMQ;

/// <summary>
/// Thrown when a message received from the broker cannot be read as an
/// <c>IntegrationEventMessage</c> — a missing or unparsable message id, or no event name anywhere on
/// the delivery.
/// </summary>
/// <remarks>
/// It is deliberately distinct from a handler failure. A handler failure may succeed on the next
/// attempt; a malformed message will be exactly as malformed on every redelivery, so the consumer
/// dead-letters it immediately instead of spending the retry budget on it.
/// </remarks>
public sealed class RabbitMQMessageFormatException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQMessageFormatException"/> class.
    /// </summary>
    public RabbitMQMessageFormatException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQMessageFormatException"/> class.
    /// </summary>
    /// <param name="message">The message that describes what was wrong with the delivery.</param>
    public RabbitMQMessageFormatException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQMessageFormatException"/> class.
    /// </summary>
    /// <param name="message">The message that describes what was wrong with the delivery.</param>
    /// <param name="innerException">The failure that caused it.</param>
    public RabbitMQMessageFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
