namespace Bsft.Messaging.RabbitMQ.Consuming;

/// <summary>
/// Where the subscription puts the messages it receives. It is the inversion that turns RabbitMQ's push
/// delivery into the pull stream <c>IMessageSource</c> is.
/// </summary>
/// <remarks>
/// The broker pushes, and the client raises an event per message; the consumer host asks for the next
/// message and holds it until it has decided. Something has to sit between the two, and this is the
/// half of it the subscription sees: the consumer hands every delivery here and is done with it, and
/// the source on the other side decides when the host gets to see it.
/// </remarks>
internal interface IRabbitMQDeliverySink
{
    /// <summary>
    /// Takes one delivery from the subscription.
    /// </summary>
    /// <param name="channel">The channel it arrived on, which is the only channel it can be settled on.</param>
    /// <param name="delivery">The delivery as it came off the wire.</param>
    /// <param name="cancellationToken">A token that is cancelled when the consumer stops.</param>
    /// <returns>
    /// A task that completes once the delivery has been taken. It is awaited by the client's delivery
    /// callback, so a sink that waits for room is what holds the broker back.
    /// </returns>
    Task TakeAsync(IRabbitMQChannel channel, RabbitMQDelivery delivery, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports that the subscription is gone and could not be put back, so the stream must end.
    /// </summary>
    /// <remarks>
    /// The consumer re-subscribes by itself after the client recovered a connection, and that is the one
    /// failure it cannot retry its way out of: the connection is healthy, so no further recovery will be
    /// reported and nothing would ask again. Telling the sink is what turns it into a stream that ends,
    /// which is the only signal the consumer host acts on — it waits out its restart delay and
    /// enumerates the source again, and that enumeration subscribes from scratch.
    /// </remarks>
    /// <param name="failure">What made the subscription unrecoverable, reported on the stream.</param>
    void Fault(Exception failure);
}
