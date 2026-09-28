using Bsft.Messaging.RabbitMQ.Connections;
using Microsoft.Extensions.Logging;

namespace Bsft.Messaging.RabbitMQ.Consuming;

/// <summary>
/// Establishes a consuming channel: prefetch, topology, subscription — in that order, and all or
/// nothing.
/// </summary>
internal sealed class RabbitMQConsumerChannelFactory : IRabbitMQConsumerChannelFactory
{
    private readonly IRabbitMQTopologyDeclarer _declarer;
    private readonly RabbitMQTopology _topology;
    private readonly RabbitMQConnectionSettings _settings;
    private readonly ILogger<RabbitMQConsumerChannelFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQConsumerChannelFactory"/> class.
    /// </summary>
    /// <param name="declarer">The declarer creating the queue, its bindings and the dead-letter pair.</param>
    /// <param name="topology">The resolved names, supplying the queue to consume from.</param>
    /// <param name="settings">The settings, supplying the prefetch count.</param>
    /// <param name="logger">The logger used to report an established subscription.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public RabbitMQConsumerChannelFactory(
        IRabbitMQTopologyDeclarer declarer,
        RabbitMQTopology topology,
        RabbitMQConnectionSettings settings,
        ILogger<RabbitMQConsumerChannelFactory> logger)
    {
        _declarer = declarer ?? throw new ArgumentNullException(nameof(declarer));
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public async Task<IRabbitMQChannel> CreateSubscribedChannelAsync(
        IRabbitMQConnection connection,
        Func<IRabbitMQChannel, RabbitMQDelivery, Task> onDeliveryAsync,
        CancellationToken cancellationToken = default)
    {
        if (connection is null)
            throw new ArgumentNullException(nameof(connection));
        if (onDeliveryAsync is null)
            throw new ArgumentNullException(nameof(onDeliveryAsync));

        // No publisher confirmations: this channel only consumes, and confirmations are a cost paid per
        // publish for a guarantee a consumer has no use for.
        var channel = await connection.CreateChannelAsync(publisherConfirmations: false, cancellationToken).ConfigureAwait(false);
        var subscribed = false;

        try
        {
            // The prefetch is applied before the subscription, not after: between the two the broker
            // would be free to push the entire queue at this consumer.
            await channel.SetPrefetchAsync(_settings.PrefetchCount, cancellationToken).ConfigureAwait(false);
            await _declarer.DeclareConsumeTopologyAsync(channel, cancellationToken).ConfigureAwait(false);
            await channel
                .ConsumeAsync(_topology.QueueName, delivery => onDeliveryAsync(channel, delivery), cancellationToken)
                .ConfigureAwait(false);

            subscribed = true;
        }
        finally
        {
            // A channel that did not get all the way to a subscription is closed again rather than
            // kept: a half-established consumer would look alive while delivering nothing.
            if (!subscribed)
                await channel.DisposeAsync().ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Consuming queue {Queue} bound to exchange {Exchange} with a prefetch of {PrefetchCount}.",
            _topology.QueueName,
            _topology.ExchangeName,
            _settings.PrefetchCount);

        return channel;
    }
}
