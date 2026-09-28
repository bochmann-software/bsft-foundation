using RabbitMQ.Client;

namespace Bsft.Messaging.RabbitMQ;

/// <summary>
/// Translates between the framework's wire envelope and AMQP. It is the one place that decides which
/// envelope field lives in which AMQP property, so the publishing and the consuming side cannot
/// disagree about it.
/// </summary>
internal interface IRabbitMQMessageMapper
{
    /// <summary>Builds the AMQP properties for an outgoing message.</summary>
    /// <param name="message">The message to publish.</param>
    /// <returns>The properties to send with it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is null.</exception>
    BasicProperties ToProperties(IntegrationEventMessage message);

    /// <summary>Encodes the payload of an outgoing message into the AMQP body.</summary>
    /// <param name="message">The message to publish.</param>
    /// <returns>The body bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is null.</exception>
    ReadOnlyMemory<byte> ToBody(IntegrationEventMessage message);

    /// <summary>Reads a delivery back into the wire envelope.</summary>
    /// <param name="delivery">The delivery as it arrived from the broker.</param>
    /// <returns>The envelope the delivery carries.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="delivery"/> is null.</exception>
    /// <exception cref="Bsft.Messaging.RabbitMQ.RabbitMQMessageFormatException">
    /// Thrown when the delivery carries no usable event id or no event name.
    /// </exception>
    IntegrationEventMessage ToMessage(RabbitMQDelivery delivery);

    /// <summary>
    /// Reads the one-based number of this delivery attempt from the broker's redelivery count.
    /// </summary>
    /// <param name="delivery">The delivery as it arrived from the broker.</param>
    /// <returns>
    /// The attempt number: 1 for a first delivery, and one more than the count the broker reports for a
    /// redelivery. A queue that reports no count at all yields 1 every time.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="delivery"/> is null.</exception>
    int ReadDeliveryAttempt(RabbitMQDelivery delivery);

    /// <summary>
    /// Builds the AMQP properties for republishing a delivery to the dead-letter exchange, carrying the
    /// reason it was parked for.
    /// </summary>
    /// <param name="delivery">The delivery being parked.</param>
    /// <param name="reason">Why it was parked.</param>
    /// <returns>The properties to publish the parked copy with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    BasicProperties ToParkProperties(RabbitMQDelivery delivery, string reason);
}
