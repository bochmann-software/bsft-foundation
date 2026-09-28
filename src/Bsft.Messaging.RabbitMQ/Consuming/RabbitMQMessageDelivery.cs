using Bsft.Messaging.Consuming;

namespace Bsft.Messaging.RabbitMQ.Consuming;

/// <summary>
/// One AMQP delivery presented to the consumer host as an <see cref="IMessageDelivery"/>.
/// </summary>
/// <remarks>
/// <para>
/// It holds the channel the message arrived on and hands it back with every completion, which is what
/// keeps a delivery tag being settled on the channel that issued it even after a reconnect has replaced
/// the current one. Nothing else here decides anything: the three completions are the host's decisions,
/// and this only names them in AMQP.
/// </para>
/// <para>
/// The one exception is the retry delay, because AMQP has no word for it. A requeue is immediate, and
/// the only way to postpone a redelivery is a topology built for it — a dead-letter exchange with a
/// message TTL, or the delayed-message plugin — which this adapter will not declare on a host's broker
/// as a side effect of a failed message. So the wait is taken here, before the nack, and the consumer
/// host is held for its duration exactly as it was before the delay became a parameter.
/// <c>ConsumerOptions.MaxRetryDelay</c> is the bound on that, and a deployment that needs longer waits
/// wants a retry topology rather than a larger number.
/// </para>
/// </remarks>
internal sealed class RabbitMQMessageDelivery : IMessageDelivery
{
    private readonly IRabbitMQDeliverySettler _settler;
    private readonly IRabbitMQChannel _channel;
    private readonly RabbitMQDelivery _delivery;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQMessageDelivery"/> class.
    /// </summary>
    /// <param name="settler">What tells the broker the outcome.</param>
    /// <param name="channel">The channel the delivery arrived on.</param>
    /// <param name="delivery">The delivery as it came off the wire.</param>
    /// <param name="mapper">The mapper reading the envelope and the attempt number out of it.</param>
    /// <param name="timeProvider">The clock the retry delay is waited on.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="Bsft.Messaging.RabbitMQ.RabbitMQMessageFormatException">
    /// Thrown when the delivery carries no readable envelope. Constructing this is therefore also the
    /// check for whether the host can be shown the message at all — there is nothing to show it
    /// otherwise, since the envelope is what a delivery is made of.
    /// </exception>
    public RabbitMQMessageDelivery(
        IRabbitMQDeliverySettler settler,
        IRabbitMQChannel channel,
        RabbitMQDelivery delivery,
        IRabbitMQMessageMapper mapper,
        TimeProvider timeProvider)
    {
        _settler = settler ?? throw new ArgumentNullException(nameof(settler));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (mapper is null)
            throw new ArgumentNullException(nameof(mapper));

        // Read once, here, rather than on each access: the envelope is what the host decides by, and
        // re-deriving it per property would let the same delivery answer differently.
        Message = mapper.ToMessage(delivery);
        DeliveryAttempt = mapper.ReadDeliveryAttempt(delivery);
    }

    /// <inheritdoc />
    public IntegrationEventMessage Message { get; }

    /// <inheritdoc />
    public int DeliveryAttempt { get; }

    /// <inheritdoc />
    public Task AcknowledgeAsync(CancellationToken cancellationToken = default) =>
        _settler.AcknowledgeAsync(_channel, _delivery, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The wait is taken before the nack rather than scheduled, for the reason on the class: nothing in
    /// AMQP postpones a requeue. Holding the message unsettled for the duration is the honest substitute
    /// — the broker keeps it, so nothing is lost if this consumer dies while it waits — and the cost is
    /// the consumer host waiting with it.
    /// </remarks>
    public async Task RetryAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);

        await _settler.RequeueAsync(_channel, _delivery, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task ParkAsync(string reason, CancellationToken cancellationToken = default) =>
        reason is null
            ? throw new ArgumentNullException(nameof(reason))
            : _settler.ParkAsync(_channel, _delivery, reason, cancellationToken);
}
