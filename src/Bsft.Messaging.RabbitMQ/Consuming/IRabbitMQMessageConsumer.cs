namespace Bsft.Messaging.RabbitMQ.Consuming;

/// <summary>
/// Consumes this application's queue: declares what it needs, subscribes, keeps the subscription alive
/// across reconnects, and settles the deliveries it made.
/// </summary>
/// <remarks>
/// Settling is part of this rather than a component of its own because it cannot be anything else. A
/// delivery tag is meaningful only on the channel that issued it, and this is what owns the channels —
/// which is the same reason <see cref="IRabbitMQDeliverySettler"/> takes one.
/// </remarks>
internal interface IRabbitMQMessageConsumer : IRabbitMQDeliverySettler, IAsyncDisposable
{
    /// <summary>Gets whether the consumer currently holds a subscription.</summary>
    bool IsConsuming { get; }

    /// <summary>
    /// Opens the connection, declares the consume topology and starts handing deliveries to the sink.
    /// Starting an already started consumer does nothing.
    /// </summary>
    /// <param name="sink">Where received deliveries go.</param>
    /// <param name="cancellationToken">A cancellation token to observe while starting.</param>
    /// <returns>A task that completes once the subscription is in place.</returns>
    Task StartAsync(IRabbitMQDeliverySink sink, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the subscription and closes the channel and connection. Stopping a consumer that is not
    /// running does nothing.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe while stopping.</param>
    /// <returns>A task that completes once everything is closed.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}
