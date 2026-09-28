using Bsft.Messaging.Consuming;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Bsft.Messaging.RabbitMQ.Consuming;

/// <summary>
/// The RabbitMQ <see cref="IMessageSource"/>: it subscribes for as long as the consumer host enumerates
/// it, and turns the broker's pushed deliveries into the stream the host pulls from.
/// </summary>
/// <remarks>
/// <para>
/// The two halves of the pipeline disagree about who is in control, and this is where that is settled.
/// RabbitMQ pushes: the client raises an event per message, as many as the prefetch allows, and the
/// callback is done when it returns. The host pulls: it asks for the next message and holds it until it
/// has decided. An in-process queue sits between them, sized to the prefetch — which is the number of
/// messages the broker will have outstanding anyway, so the queue can never be the thing that stalls a
/// delivery callback.
/// </para>
/// <para>
/// One enumeration is one subscription. Starting it opens the connection and subscribes; leaving it —
/// because the host is stopping, or because the stream is being re-established — cancels the
/// subscription and closes both. That is what makes a failure to connect visible as a stream that
/// throws, which the host logs and retries after <c>ReceiveRestartDelay</c>: the reconnect loop the
/// seam describes is the host's, and this does not need one of its own. Reconnects <em>within</em> a
/// live connection are the client's, and the consumer re-subscribes on them without the stream ending.
/// </para>
/// <para>
/// It maps the AMQP framing onto the envelope and nothing beyond it. Decoding the payload into an
/// event, resolving its name, deduplicating it through the inbox and running handlers are all on the
/// host's side of the seam. The one message the host never sees is one that is not an envelope at all:
/// it is parked here, because there is no <see cref="IMessageDelivery"/> to hand over for it and a
/// redelivery of the same bytes would be exactly as unreadable.
/// </para>
/// </remarks>
internal sealed class RabbitMQMessageSource : IMessageSource, IRabbitMQDeliverySink
{
    private readonly IRabbitMQMessageConsumer _consumer;
    private readonly IRabbitMQMessageMapper _mapper;
    private readonly RabbitMQConnectionSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RabbitMQMessageSource> _logger;

    private ChannelWriter<IMessageDelivery>? _writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQMessageSource"/> class.
    /// </summary>
    /// <param name="consumer">The subscription, which is also what settles the deliveries it made.</param>
    /// <param name="mapper">The mapper reading a delivery into the wire envelope.</param>
    /// <param name="settings">The settings, supplying the prefetch the in-process queue is sized to.</param>
    /// <param name="timeProvider">The clock a delivery waits its retry delay on before it is requeued.</param>
    /// <param name="logger">The logger used to report unreadable and undeliverable messages.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public RabbitMQMessageSource(
        IRabbitMQMessageConsumer consumer,
        IRabbitMQMessageMapper mapper,
        RabbitMQConnectionSettings settings,
        TimeProvider timeProvider,
        ILogger<RabbitMQMessageSource> logger)
    {
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IMessageDelivery> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Bounded at the prefetch, not unbounded: the queue must not become a second buffer in front of
        // the broker's own. At the prefetch it never actually blocks a delivery callback, because the
        // broker will not have more than that many messages outstanding — the bound is there so that a
        // prefetch raised without thinking cannot turn into unbounded memory instead of backpressure.
        var queue = Channel.CreateBounded<IMessageDelivery>(
            new BoundedChannelOptions(_settings.PrefetchCount)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        _writer = queue.Writer;

        try
        {
            await _consumer.StartAsync(this, cancellationToken).ConfigureAwait(false);

            // Endless on purpose: the queue is completed without a failure only in the finally below, after
            // the enumeration has already ended, so the stream ends by a fault, a cancellation or the
            // caller disposing it, never by running dry.
            while (true)
                yield return await NextAsync(queue.Reader, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Cleared before the subscription is cancelled, so a delivery racing the shutdown finds no
            // stream to be put on and is left unsettled for the broker to deliver again, rather than
            // being written to a queue nobody will read.
            _writer = null;
            queue.Writer.TryComplete();

            // Not the caller's token: it is cancelled precisely when the host is shutting down, and
            // closing the connection is the part of that which still has to happen.
            await _consumer.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Waits for the next delivery on the queue.</summary>
    /// <param name="reader">The queue's reader.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The next delivery.</returns>
    /// <exception cref="ChannelClosedException">
    /// Thrown when the queue was completed without a failure, which only happens once the enumeration has ended.
    /// </exception>
    /// <remarks>
    /// A queue faulted by <see cref="Fault"/> surfaces the failure itself rather than the
    /// <see cref="ChannelClosedException"/> the channel wraps it in, because the failure is what the host's
    /// restart loop logs and reacts to.
    /// </remarks>
    private static async ValueTask<IMessageDelivery> NextAsync(ChannelReader<IMessageDelivery> reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException closed) when (closed.InnerException is { } failure)
        {
            throw failure;
        }
    }

    /// <inheritdoc />
    public async Task TakeAsync(
        IRabbitMQChannel channel, RabbitMQDelivery delivery, CancellationToken cancellationToken = default)
    {
        if (channel is null)
            throw new ArgumentNullException(nameof(channel));
        if (delivery is null)
            throw new ArgumentNullException(nameof(delivery));

        var writer = _writer;

        if (writer is null)
        {
            _logger.LogDebug(
                "Delivery {DeliveryTag} arrived while no consumer was enumerating this source; it stays unsettled and the broker will deliver it again.",
                delivery.DeliveryTag);

            return;
        }

        if (!TryPresent(channel, delivery, out var queued))
        {
            await ParkUnreadableAsync(channel, delivery, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await writer.WriteAsync(queued, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // The stream ended between the null check above and this write. Leaving the delivery
            // unsettled is the correct outcome and the only available one: the broker still holds it.
            _logger.LogDebug(
                "Delivery {DeliveryTag} arrived as the stream was ending; it stays unsettled and the broker will deliver it again.",
                delivery.DeliveryTag);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Delivery {DeliveryTag} arrived while the consumer was stopping; it stays unsettled and the broker will deliver it again.",
                delivery.DeliveryTag);
        }
    }

    /// <inheritdoc />
    public void Fault(Exception failure)
    {
        if (failure is null)
            throw new ArgumentNullException(nameof(failure));

        var writer = _writer;

        if (writer is null)
        {
            // Nothing is enumerating, so there is no stream to end. The next enumeration subscribes
            // from scratch anyway, which is the state this call was trying to reach.
            _logger.LogDebug(
                "The subscription was lost while no consumer was enumerating this source; the next enumeration subscribes again.");

            return;
        }

        // Faulted rather than completed, so the host logs what went wrong instead of treating it as a
        // source that ended by choice. It is a TryComplete because the enumeration may have ended
        // between reading the field and this call, and a stream that has already ended needs nothing.
        writer.TryComplete(failure);
    }

    /// <summary>
    /// Builds what the host is shown for a delivery, reporting rather than raising when the delivery
    /// carries nothing that can be shown.
    /// </summary>
    private bool TryPresent(
        IRabbitMQChannel channel, RabbitMQDelivery delivery, out RabbitMQMessageDelivery presented)
    {
        try
        {
            presented = new RabbitMQMessageDelivery(_consumer, channel, delivery, _mapper, _timeProvider);
            return true;
        }
        catch (RabbitMQMessageFormatException malformed)
        {
            _logger.LogError(
                malformed,
                "A delivery with tag {DeliveryTag} on routing key {RoutingKey} could not be read as an integration event and is parked without reaching the consumer.",
                delivery.DeliveryTag,
                delivery.RoutingKey);

            presented = null!;
            return false;
        }
    }

    /// <summary>
    /// Parks a delivery the host can never be shown. It is the transport's own park rather than the
    /// host's, and it is not retried: the same bytes would be exactly as unreadable next time, so
    /// requeueing would spend the queue on a message nothing can handle.
    /// </summary>
    private Task ParkUnreadableAsync(
        IRabbitMQChannel channel, RabbitMQDelivery delivery, CancellationToken cancellationToken) =>
        _consumer.ParkAsync(
            channel,
            delivery,
            // Category, colon, detail — the shape the consumer host writes its own park reasons in, so
            // everything in the dead-letter queue's x-park-reason header reads the same way and its
            // leading token is always a MessageParkReason.
            $"{MessageParkReason.UnreadableEnvelope}: the delivery on routing key '{delivery.RoutingKey}' does not carry a readable integration event envelope.",
            cancellationToken);
}
