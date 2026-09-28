using RabbitMQ.Client;

namespace Bsft.Messaging.RabbitMQ;

/// <summary>
/// The slice of an AMQP channel this adapter uses, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// It exists for one reason: there is no broker in development or in CI, and <c>IChannel</c> cannot be
/// stood in for — its methods carry optional parameters and a generic publish, and its implementations
/// expect a live protocol handshake. Narrowing it to the eight operations this adapter performs makes
/// the interesting parts — the property mapping, the confirm handling, the topology declaration and
/// the acknowledgement decisions — testable against a fake, and leaves only the forwarding in
/// <see cref="Bsft.Messaging.RabbitMQ.Connections.RabbitMQChannelAdapter"/> unverified without a broker.
/// </para>
/// <para>
/// The AMQP property type is deliberately the client's own <see cref="BasicProperties"/> rather than a
/// type of ours. It is a plain settable class that needs no connection to construct, so a test can
/// assert the exact properties that would go on the wire instead of asserting a translation of them.
/// </para>
/// </remarks>
internal interface IRabbitMQChannel : IAsyncDisposable
{
    /// <summary>Gets whether the channel is usable.</summary>
    bool IsOpen { get; }

    /// <summary>
    /// Declares a durable exchange. Declaring one that already exists with the same arguments does
    /// nothing, which is what lets the declaration run on every start.
    /// </summary>
    /// <param name="exchange">The exchange name.</param>
    /// <param name="type">The exchange type, e.g. <see cref="ExchangeType.Topic"/>.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the broker has accepted the declaration.</returns>
    Task DeclareExchangeAsync(string exchange, string type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Declares a durable queue. As with an exchange, re-declaring an identical queue is a no-op.
    /// </summary>
    /// <param name="queue">The queue name.</param>
    /// <param name="arguments">The queue arguments, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the broker has accepted the declaration.</returns>
    Task DeclareQueueAsync(string queue, IDictionary<string, object?>? arguments, CancellationToken cancellationToken = default);

    /// <summary>Binds a queue to an exchange under a routing key.</summary>
    /// <param name="queue">The queue to bind.</param>
    /// <param name="exchange">The exchange to bind it to.</param>
    /// <param name="routingKey">The routing key of the binding.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the broker has accepted the binding.</returns>
    Task BindQueueAsync(string queue, string exchange, string routingKey, CancellationToken cancellationToken = default);

    /// <summary>Limits how many unacknowledged messages the broker may have outstanding on this channel.</summary>
    /// <param name="prefetchCount">The maximum number of unacknowledged messages.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the broker has applied the limit.</returns>
    Task SetPrefetchAsync(ushort prefetchCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a message and completes when the broker has confirmed it.
    /// </summary>
    /// <remarks>
    /// The returned task is the whole point of the seam. On a channel opened with confirmations it does
    /// not complete when the bytes are written but when the broker has taken responsibility for the
    /// message, and it faults when the broker refused it — which is what allows the outbox to treat a
    /// returned publish as "this event is now the broker's problem".
    /// </remarks>
    /// <param name="exchange">The exchange to publish to.</param>
    /// <param name="routingKey">The routing key, by convention the event name.</param>
    /// <param name="properties">The AMQP properties to send.</param>
    /// <param name="body">The message body.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes once the broker has confirmed the message.</returns>
    Task PublishAsync(
        string exchange,
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts delivering messages from a queue to a callback, with acknowledgement left to the caller.
    /// </summary>
    /// <param name="queue">The queue to consume from.</param>
    /// <param name="onDeliveryAsync">The callback invoked for every delivery.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The consumer tag the broker assigned.</returns>
    Task<string> ConsumeAsync(
        string queue, Func<RabbitMQDelivery, Task> onDeliveryAsync, CancellationToken cancellationToken = default);

    /// <summary>Acknowledges a delivery, so the broker may forget the message.</summary>
    /// <param name="deliveryTag">The tag of the delivery to acknowledge.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the acknowledgement has been sent.</returns>
    Task AckAsync(ulong deliveryTag, CancellationToken cancellationToken = default);

    /// <summary>Rejects a delivery.</summary>
    /// <param name="deliveryTag">The tag of the delivery to reject.</param>
    /// <param name="requeue">
    /// Whether the broker should put the message back on the queue. Rejecting without requeue is what
    /// routes the message to the dead-letter exchange.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the rejection has been sent.</returns>
    Task NackAsync(ulong deliveryTag, bool requeue, CancellationToken cancellationToken = default);
}
