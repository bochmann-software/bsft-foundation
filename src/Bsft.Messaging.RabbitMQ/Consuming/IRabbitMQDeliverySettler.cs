namespace Bsft.Messaging.RabbitMQ.Consuming;

/// <summary>
/// Tells the broker what became of a delivery. It is the AMQP end of the three completions on
/// <c>IMessageDelivery</c>, one method each.
/// </summary>
/// <remarks>
/// <para>
/// Every method takes the channel the delivery arrived on, and that is the whole reason this seam has a
/// shape of its own. A delivery tag means something only on the channel that issued it, and the same
/// number is reused on the next one, so a settlement that went to whichever channel is current would
/// acknowledge an unrelated message after a reconnect. The implementation drops a settlement whose
/// channel is no longer the one being consumed; the message stays unsettled and the broker delivers it
/// again, which is the outcome at-least-once delivery already accounts for.
/// </para>
/// <para>
/// No method throws for a broker that went away. A completion is called from the consumer host's loop
/// while it holds a message, and a failure there would unwind a loop whose entire purpose is not to be
/// unwound by one message.
/// </para>
/// </remarks>
internal interface IRabbitMQDeliverySettler
{
    /// <summary>Acknowledges a delivery, so the broker may forget the message.</summary>
    /// <param name="channel">The channel the delivery arrived on.</param>
    /// <param name="delivery">The delivery to acknowledge.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the acknowledgement has been sent or dropped.</returns>
    Task AcknowledgeAsync(IRabbitMQChannel channel, RabbitMQDelivery delivery, CancellationToken cancellationToken = default);

    /// <summary>Rejects a delivery with requeue, so the broker delivers it again.</summary>
    /// <param name="channel">The channel the delivery arrived on.</param>
    /// <param name="delivery">The delivery to hand back.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the rejection has been sent or dropped.</returns>
    Task RequeueAsync(IRabbitMQChannel channel, RabbitMQDelivery delivery, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a delivery aside where an operator can still find it, recording why.
    /// </summary>
    /// <param name="channel">The channel the delivery arrived on.</param>
    /// <param name="delivery">The delivery to park.</param>
    /// <param name="reason">Why it was parked, as the consumer host described it.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the message has been preserved and the original settled.</returns>
    Task ParkAsync(
        IRabbitMQChannel channel, RabbitMQDelivery delivery, string reason, CancellationToken cancellationToken = default);
}
