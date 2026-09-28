using Bsft.Messaging.RabbitMQ.Connections;

namespace Bsft.Messaging.RabbitMQ.Consuming;

/// <summary>
/// Turns a connection into a channel that is actually consuming: prefetch applied, topology declared,
/// subscription in place.
/// </summary>
/// <remarks>
/// The three steps belong together and are needed at two different moments — at startup and again
/// after a recovered connection — so they live here rather than in the consumer. It also keeps the
/// consumer out of the naming and the prefetch entirely: it asks for a subscribed channel and gets one.
/// </remarks>
internal interface IRabbitMQConsumerChannelFactory
{
    /// <summary>
    /// Opens a channel on the connection, applies the prefetch, declares the consume topology and
    /// subscribes to this application's queue.
    /// </summary>
    /// <param name="connection">The connection to open the channel on.</param>
    /// <param name="onDeliveryAsync">
    /// The callback every delivery is passed to, together with the channel it arrived on. The channel
    /// is handed over rather than looked up later because a delivery tag only means anything on the
    /// channel that issued it.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The subscribed channel.</returns>
    Task<IRabbitMQChannel> CreateSubscribedChannelAsync(
        IRabbitMQConnection connection,
        Func<IRabbitMQChannel, RabbitMQDelivery, Task> onDeliveryAsync,
        CancellationToken cancellationToken = default);
}
