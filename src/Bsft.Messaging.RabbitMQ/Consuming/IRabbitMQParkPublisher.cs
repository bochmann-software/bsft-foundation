namespace Bsft.Messaging.RabbitMQ.Consuming;

/// <summary>
/// Preserves a message the consumer host gave up on, together with the reason it gave.
/// </summary>
/// <remarks>
/// <para>
/// It exists because a rejection cannot carry a reason. Rejecting without requeue is the cheap way to
/// reach the dead-letter exchange, and it is what AMQP offers, but the only thing it tells an operator
/// is <c>rejected</c> — the sentence the host wrote about why the message can never be handled is lost
/// on the way. Republishing the message to the dead-letter exchange keeps that sentence in a header, at
/// the cost of the <c>x-death</c> records the broker would have added for a rejection.
/// </para>
/// <para>
/// It is separate from the settlement of the delivery tag: preserving a message and telling the broker
/// what happened to the original are two operations on two channels, and only their order matters —
/// see <see cref="IRabbitMQDeliverySettler.ParkAsync"/>.
/// </para>
/// </remarks>
internal interface IRabbitMQParkPublisher
{
    /// <summary>
    /// Publishes a copy of the delivery to the dead-letter exchange with the park reason attached.
    /// </summary>
    /// <param name="delivery">The delivery being parked.</param>
    /// <param name="reason">Why the consumer host parked it.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// <see langword="true"/> when the broker has confirmed the parked copy, and
    /// <see langword="false"/> when it could not be published — in which case the caller still has an
    /// unsettled message and must fall back to a rejection. It does not throw.
    /// </returns>
    Task<bool> TryPublishAsync(RabbitMQDelivery delivery, string reason, CancellationToken cancellationToken = default);
}
