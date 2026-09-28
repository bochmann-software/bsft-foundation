using Bsft.Messaging.RabbitMQ.Publishing;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Exceptions;

namespace Bsft.Messaging.RabbitMQ.Consuming;

/// <summary>
/// Republishes a parked message to the dead-letter exchange over the confirming publish channel.
/// </summary>
/// <remarks>
/// <para>
/// The publish goes over the <em>publishing</em> channel rather than the consuming one, because that
/// channel is opened with publisher confirmations. Parking is the one outcome that ends a message's
/// life with nobody having handled it, so the copy has to be known to exist before the original is
/// acknowledged — and only a confirmation knows that. On the consuming channel the publish would
/// return as soon as the bytes were written, and a broker that dropped it afterwards would leave the
/// message gone with a log line claiming it was preserved. Waiting for that confirmation is bounded by
/// <see cref="RabbitMQConnectionSettings.PublisherConfirmTimeout"/>, so a confirmation that never comes
/// falls back to the reject path below rather than stalling the consumer that is waiting on it.
/// </para>
/// <para>
/// The routing key is the one the message arrived under, which is the event name. That matches what a
/// broker-side rejection does and what the dead-letter queue's <c>#</c> binding expects, so a parked
/// message still says which event it was.
/// </para>
/// </remarks>
internal sealed class RabbitMQParkPublisher : IRabbitMQParkPublisher
{
    private readonly IRabbitMQPublishChannelProvider _channelProvider;
    private readonly IRabbitMQMessageMapper _mapper;
    private readonly RabbitMQTopology _topology;
    private readonly RabbitMQConnectionSettings _settings;
    private readonly ILogger<RabbitMQParkPublisher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQParkPublisher"/> class.
    /// </summary>
    /// <param name="channelProvider">The provider of the confirming publish channel.</param>
    /// <param name="mapper">The mapper building the properties of the parked copy.</param>
    /// <param name="topology">The resolved names, supplying the dead-letter exchange.</param>
    /// <param name="settings">The settings, supplying the confirm timeout.</param>
    /// <param name="logger">The logger used to report parked messages and failed preservations.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public RabbitMQParkPublisher(
        IRabbitMQPublishChannelProvider channelProvider,
        IRabbitMQMessageMapper mapper,
        RabbitMQTopology topology,
        RabbitMQConnectionSettings settings,
        ILogger<RabbitMQParkPublisher> logger)
    {
        _channelProvider = channelProvider ?? throw new ArgumentNullException(nameof(channelProvider));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> TryPublishAsync(
        RabbitMQDelivery delivery, string reason, CancellationToken cancellationToken = default)
    {
        if (delivery is null)
            throw new ArgumentNullException(nameof(delivery));
        if (reason is null)
            throw new ArgumentNullException(nameof(reason));

        try
        {
            var properties = _mapper.ToParkProperties(delivery, reason);
            var channel = await _channelProvider.GetChannelAsync(cancellationToken).ConfigureAwait(false);

            // Only the publish runs under the confirm window, as on the message bus: opening a
            // connection has timeouts of its own, and folding it in here would report a broker that is
            // merely slow to connect as a confirmation that never came. The window is what stops a
            // broker that holds the socket open but stops confirming — a resource alarm does exactly
            // that — from blocking this park forever. The caller's token is the host's stopping token,
            // which nothing cancels while the host runs, so without the window there is no deadline at
            // all, and parking is the terminal step of a strictly sequential consume loop.
            using var confirmWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            confirmWindow.CancelAfter(_settings.PublisherConfirmTimeout);

            await channel
                .PublishAsync(
                    _topology.DeadLetterExchangeName,
                    delivery.RoutingKey,
                    properties,
                    delivery.Body,
                    confirmWindow.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException unconfirmed) when (!cancellationToken.IsCancellationRequested)
        {
            // The confirm window closed rather than the caller withdrawing the park.
            ReportUnpreserved(delivery, unconfirmed);
            return false;
        }
        catch (RabbitMQClientException refused)
        {
            ReportUnpreserved(delivery, refused);
            return false;
        }
        catch (IOException unreachable)
        {
            // BrokerUnreachableException is an IOException rather than a RabbitMQClientException, and
            // a socket that dies mid-handshake surfaces as one too.
            ReportUnpreserved(delivery, unreachable);
            return false;
        }
        catch (TimeoutException unresponsive)
        {
            // The client raises a plain TimeoutException when one of its own RPCs runs past the
            // continuation timeout, which is neither of the two above.
            ReportUnpreserved(delivery, unresponsive);
            return false;
        }
        catch (ObjectDisposedException shuttingDown)
        {
            // The publishing side was disposed while this park was in flight, which a shutdown that
            // reaches the publish channel provider before the consumer does will do. Reporting it as an
            // unpreserved park is what keeps the promise this method makes: it does not throw, because
            // the only caller is a consumer host loop that must not be unwound by one message.
            ReportUnpreserved(delivery, shuttingDown);
            return false;
        }

        // The reason itself is deliberately not logged here. It is derived from an exception and can
        // quote the event, so it travels exactly one way — onto the parked message, bounded — and the
        // consumer host has already written it to its own log with the message's identity next to it.
        // Repeating it would put payload-derived text into a second sink for every ordinary handler
        // failure, which is the one thing this reason is not supposed to do.
        _logger.LogWarning(
            "Delivery {DeliveryTag} on routing key {RoutingKey} was parked to {DeadLetterExchange}; the reason travels with the message in the {ParkReasonHeader} header.",
            delivery.DeliveryTag,
            delivery.RoutingKey,
            _topology.DeadLetterExchangeName,
            RabbitMQMessageMapper.ParkReasonHeader);

        return true;
    }

    /// <summary>
    /// Reports that the copy did not reach the broker.
    /// </summary>
    /// <remarks>
    /// Nothing is lost by the park failing here: the original delivery is still unsettled, and the
    /// caller rejects it instead, which routes it to the same exchange through the broker's own
    /// dead-lettering. Only the reason does not make it, which is why this is reported loudly rather
    /// than at debug — a dead-letter queue filling up with entries whose reason is missing is a symptom
    /// worth chasing.
    /// </remarks>
    /// <param name="delivery">The delivery whose parked copy was refused.</param>
    /// <param name="failure">What the broker or the client raised.</param>
    private void ReportUnpreserved(RabbitMQDelivery delivery, Exception failure)
    {
        _logger.LogError(
            failure,
            "Parking delivery {DeliveryTag} on routing key {RoutingKey} could not be published to {DeadLetterExchange}; it is rejected instead and the park reason is not preserved.",
            delivery.DeliveryTag,
            delivery.RoutingKey,
            _topology.DeadLetterExchangeName);
    }
}
