namespace Bsft.Messaging.RabbitMQ.Connections;

/// <summary>
/// Creates the broker objects the adapter needs. Every declaration is idempotent, so it runs on every
/// start and after every reconnect rather than being a deployment step somebody has to remember.
/// </summary>
internal interface IRabbitMQTopologyDeclarer
{
    /// <summary>
    /// Declares what publishing needs: the topic exchange, and nothing more. A publisher does not
    /// create the queues of its consumers — which of them exist is theirs to decide, and a producer
    /// that declared them would recreate a queue an operator had just deleted.
    /// </summary>
    /// <param name="channel">The channel to declare on.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the broker has accepted the declaration.</returns>
    Task DeclarePublishTopologyAsync(IRabbitMQChannel channel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Declares what consuming needs: the exchange to bind to, the dead-letter exchange and its queue,
    /// this application's queue pointing at that dead-letter exchange, and one binding per subscribed
    /// event name.
    /// </summary>
    /// <param name="channel">The channel to declare on.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the broker has accepted every declaration.</returns>
    Task DeclareConsumeTopologyAsync(IRabbitMQChannel channel, CancellationToken cancellationToken = default);
}
