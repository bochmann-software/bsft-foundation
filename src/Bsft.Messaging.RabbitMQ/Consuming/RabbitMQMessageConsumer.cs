using Bsft.Messaging.RabbitMQ.Connections;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Exceptions;

namespace Bsft.Messaging.RabbitMQ.Consuming;

/// <summary>
/// Consumes this application's queue with manual acknowledgement, puts the subscription back after the
/// client has recovered a dropped connection, and settles every delivery on the channel it arrived on.
/// </summary>
/// <remarks>
/// <para>
/// Acknowledgement is manual, and that is the point. A message is settled after the host has decided
/// about it, never before, so a consumer that dies mid-handling leaves the message on the queue instead
/// of having already told the broker to forget it. That is the consuming half of at-least-once
/// delivery, and it is what makes the prefetch meaningful: the broker counts exactly the messages this
/// consumer has taken but not yet finished.
/// </para>
/// <para>
/// Recovery is handled rather than assumed. The client reconnects and brings the channel back, but the
/// subscription and the topology were put there by this adapter and are gone with the old channel, so
/// both are established again through the same code path that established them at startup. Doing it
/// here rather than leaving it to the client's topology recovery keeps one component in charge of what
/// the topology is.
/// </para>
/// <para>
/// Nothing here decides what a message <em>means</em>. Deliveries go to the sink and come back as one
/// of three settlements; decoding the payload, deduplicating it and running handlers all happen on the
/// consumer host's side of the transport seam, which this adapter neither calls into nor knows about.
/// </para>
/// </remarks>
internal sealed class RabbitMQMessageConsumer : IRabbitMQMessageConsumer
{
    private readonly IRabbitMQConnectionFactory _connectionFactory;
    private readonly IRabbitMQConsumerChannelFactory _channelFactory;
    private readonly IRabbitMQParkPublisher _parkPublisher;
    private readonly ILogger<RabbitMQMessageConsumer> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IRabbitMQConnection? _connection;
    private IRabbitMQChannel? _channel;
    private IRabbitMQDeliverySink? _sink;
    private CancellationTokenSource? _stopping;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQMessageConsumer"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory opening the consuming connection.</param>
    /// <param name="channelFactory">The factory establishing a subscribed channel on it.</param>
    /// <param name="parkPublisher">The publisher preserving a parked message with its reason.</param>
    /// <param name="logger">The logger used to report recovery and acknowledgement failures.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public RabbitMQMessageConsumer(
        IRabbitMQConnectionFactory connectionFactory,
        IRabbitMQConsumerChannelFactory channelFactory,
        IRabbitMQParkPublisher parkPublisher,
        ILogger<RabbitMQMessageConsumer> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _parkPublisher = parkPublisher ?? throw new ArgumentNullException(nameof(parkPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsConsuming => _channel is not null;

    /// <inheritdoc />
    public async Task StartAsync(IRabbitMQDeliverySink sink, CancellationToken cancellationToken = default)
    {
        if (sink is null)
            throw new ArgumentNullException(nameof(sink));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_channel is not null)
                return;

            var stopping = new CancellationTokenSource();
            var started = false;

            try
            {
                _stopping = stopping;
                _sink = sink;

                _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                _connection.RecoverySucceeded = OnRecoverySucceededAsync;

                _channel = await SubscribeAsync(_connection, sink, stopping.Token, cancellationToken).ConfigureAwait(false);

                started = true;
            }
            finally
            {
                // A start that failed half-way has to leave nothing behind. Without this, a failing
                // declaration would keep the connection open with the recovery handler still attached,
                // and the next attempt would overwrite the fields and leak both — once per retry, for
                // as long as the host keeps trying.
                if (!started)
                    await AbandonStartAsync(stopping).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stopping = _stopping;
            if (stopping is null)
                return;

            _stopping = null;
            _sink = null;

            // Cancelled first, so a delivery still in flight learns that it is being shut down and is
            // requeued rather than dead-lettered on its way out.
            await stopping.CancelAsync().ConfigureAwait(false);
            stopping.Dispose();

            if (_connection is not null)
                _connection.RecoverySucceeded = null;

            await DiscardChannelAsync().ConfigureAwait(false);
            await DiscardConnectionAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        // Marked disposed before stopping, so a start racing with the disposal is refused rather than
        // opening a connection nobody will close. The gate itself is never disposed: a delivery or a
        // recovery callback can still be inside it at this moment, and disposing it under them would
        // turn an orderly shutdown into an ObjectDisposedException on a background thread. A
        // SemaphoreSlim whose AvailableWaitHandle was never taken has nothing to release.
        _disposed = true;

        await StopAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task AcknowledgeAsync(
        IRabbitMQChannel channel, RabbitMQDelivery delivery, CancellationToken cancellationToken = default) =>
        SettleAsync(channel, delivery, "acknowledged", () => channel.AckAsync(delivery.DeliveryTag, cancellationToken));

    /// <inheritdoc />
    public Task RequeueAsync(
        IRabbitMQChannel channel, RabbitMQDelivery delivery, CancellationToken cancellationToken = default) =>
        SettleAsync(
            channel, delivery, "requeued", () => channel.NackAsync(delivery.DeliveryTag, requeue: true, cancellationToken));

    /// <inheritdoc />
    public async Task ParkAsync(
        IRabbitMQChannel channel, RabbitMQDelivery delivery, string reason, CancellationToken cancellationToken = default)
    {
        if (channel is null)
            throw new ArgumentNullException(nameof(channel));
        if (delivery is null)
            throw new ArgumentNullException(nameof(delivery));
        if (reason is null)
            throw new ArgumentNullException(nameof(reason));

        if (!IsCurrent(channel, delivery, "parked"))
            return;

        // Preserved first, settled second. The other order would acknowledge a message before anything
        // held a copy of it, and a park that lost the message is exactly the silent drop that parking
        // exists to avoid. The cost of this order is a duplicate in the dead-letter queue when the
        // acknowledgement is the part that fails, which an operator can see and dismiss.
        var preserved = await _parkPublisher.TryPublishAsync(delivery, reason, cancellationToken).ConfigureAwait(false);

        // Checked again: publishing and confirming took time, and a recovery in that window replaced
        // the channel this tag belongs to.
        if (!IsCurrent(channel, delivery, "parked"))
            return;

        await SettleAsync(
                channel,
                delivery,
                "parked",
                () => preserved
                    ? channel.AckAsync(delivery.DeliveryTag, cancellationToken)
                    : channel.NackAsync(delivery.DeliveryTag, requeue: false, cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Undoes a start that did not complete, so the consumer is left exactly as it was before it.
    /// </summary>
    private async Task AbandonStartAsync(CancellationTokenSource stopping)
    {
        _stopping = null;
        _sink = null;

        if (_connection is not null)
            _connection.RecoverySucceeded = null;

        await DiscardChannelAsync().ConfigureAwait(false);
        await DiscardConnectionAsync().ConfigureAwait(false);

        stopping.Dispose();
    }

    /// <summary>Asks for a subscribed channel whose deliveries come back to this consumer.</summary>
    private Task<IRabbitMQChannel> SubscribeAsync(
        IRabbitMQConnection connection,
        IRabbitMQDeliverySink sink,
        CancellationToken stoppingToken,
        CancellationToken cancellationToken) =>
        _channelFactory.CreateSubscribedChannelAsync(
            connection, (channel, delivery) => sink.TakeAsync(channel, delivery, stoppingToken), cancellationToken);

    /// <summary>
    /// Runs a settlement against the channel the delivery came from, and only that channel.
    /// </summary>
    /// <remarks>
    /// A delivery tag is only meaningful on the channel that issued it, and the same number is reused
    /// from one channel on the next. So the settlement goes to the channel the message arrived on, and
    /// is dropped entirely once that channel is no longer the one being consumed — after a stop, or
    /// after a recovery replaced it. Settling such a delivery on the current channel would settle
    /// whichever unrelated message happens to hold that tag there.
    /// </remarks>
    private async Task SettleAsync(
        IRabbitMQChannel channel, RabbitMQDelivery delivery, string outcome, Func<Task> settle)
    {
        if (channel is null)
            throw new ArgumentNullException(nameof(channel));
        if (delivery is null)
            throw new ArgumentNullException(nameof(delivery));

        if (!IsCurrent(channel, delivery, outcome))
            return;

        try
        {
            await settle().ConfigureAwait(false);
        }
        catch (RabbitMQClientException broken)
        {
            ReportUnsettled(delivery, outcome, broken);
        }
        catch (IOException broken)
        {
            // A socket that died under the settlement surfaces as one of these rather than as a
            // protocol exception.
            ReportUnsettled(delivery, outcome, broken);
        }
        catch (TimeoutException unresponsive)
        {
            // The client raises a plain TimeoutException when one of its own RPCs runs past the
            // continuation timeout, which is neither of the two above.
            ReportUnsettled(delivery, outcome, unresponsive);
        }
        catch (ObjectDisposedException goneUnderUs)
        {
            // The channel this settlement was checked against can be disposed by a concurrent stop or
            // reconnect between that check and the call. The settler promises not to throw for a broker
            // that went away, and a channel torn down under the call is that, seen from here.
            ReportUnsettled(delivery, outcome, goneUnderUs);
        }
    }

    /// <summary>
    /// Reports a settlement the broker never received.
    /// </summary>
    /// <remarks>
    /// Nothing is lost: an unsettled message goes back on the queue when the connection is torn down,
    /// and handlers are idempotent because at-least-once delivery already required them to be. What
    /// matters is that it is not raised — a settlement runs inside the client's delivery dispatch or
    /// inside the consumer host's loop, and neither is a place a dead socket may unwind.
    /// </remarks>
    /// <param name="delivery">The delivery that stays unsettled.</param>
    /// <param name="outcome">What it was going to be settled as.</param>
    /// <param name="failure">What the client raised.</param>
    private void ReportUnsettled(RabbitMQDelivery delivery, string outcome, Exception failure)
    {
        _logger.LogWarning(
            failure,
            "Delivery {DeliveryTag} on routing key {RoutingKey} could not be {Outcome}; the broker will deliver it again.",
            delivery.DeliveryTag,
            delivery.RoutingKey,
            outcome);
    }

    /// <summary>Reports whether a delivery's channel is still the one being consumed.</summary>
    private bool IsCurrent(IRabbitMQChannel channel, RabbitMQDelivery delivery, string outcome)
    {
        if (ReferenceEquals(_channel, channel))
            return true;

        _logger.LogDebug(
            "Delivery {DeliveryTag} was {Outcome} after its channel had been replaced or closed; it stays unsettled and the broker will deliver it again.",
            delivery.DeliveryTag,
            outcome);

        return false;
    }

    /// <summary>
    /// Re-establishes the subscription after the client has recovered the connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It runs on the client's recovery callback, so a failure here has nowhere to go: throwing would
    /// escape into the client's event dispatch and be reported as a callback exception, leaving this
    /// consumer silently subscribed to nothing. It is reported to the sink instead.
    /// </para>
    /// <para>
    /// Waiting for the next recovery would not do. The connection this failed on is the recovered one
    /// and it is healthy, so the client has nothing left to report and no second chance ever arrives —
    /// the consumer would stay subscribed to nothing for the lifetime of the process, with one log line
    /// to show for it. So the stream is torn down instead, which is the failure the consumer host does
    /// react to: it waits out its restart delay and enumerates the source again, and that enumeration
    /// stops this consumer and starts it from scratch. Deliveries still in flight on the discarded
    /// channel settle on nothing, which is what they already did — the settlement is dropped once its
    /// channel is no longer the one being consumed, and the broker delivers them again.
    /// </para>
    /// </remarks>
    private async Task OnRecoverySucceededAsync()
    {
        IRabbitMQDeliverySink? subscribed = null;
        Exception? lost = null;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var stopping = _stopping;
            var connection = _connection;
            subscribed = _sink;

            if (_disposed || stopping is null || connection is null || subscribed is null || stopping.IsCancellationRequested)
                return;

            lost = await ResubscribeAsync(connection, subscribed, stopping.Token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (lost is null)
            return;

        _logger.LogError(
            lost,
            "Re-subscribing after a recovered connection failed; the delivery stream is ended so the consumer host establishes it again.");

        // Outside the gate on purpose: ending the stream lets the host unwind it, and the unwinding
        // calls StopAsync on this consumer — which waits for this very gate.
        subscribed!.Fault(lost);
    }

    /// <summary>
    /// Puts the subscription back on the recovered connection, returning what stopped it rather than
    /// raising: the caller is a client callback, and the failure has to be acted on rather than thrown.
    /// </summary>
    /// <param name="connection">The recovered connection to subscribe on.</param>
    /// <param name="sink">Where the re-established subscription delivers.</param>
    /// <param name="stoppingToken">The token cancelled when this consumer is stopped.</param>
    /// <returns>What made the re-subscription fail, or <see langword="null"/> when it succeeded.</returns>
    private async Task<Exception?> ResubscribeAsync(
        IRabbitMQConnection connection, IRabbitMQDeliverySink sink, CancellationToken stoppingToken)
    {
        try
        {
            await DiscardChannelAsync().ConfigureAwait(false);
            _channel = await SubscribeAsync(connection, sink, stoppingToken, stoppingToken).ConfigureAwait(false);

            _logger.LogInformation("The connection was recovered and the subscription re-established.");

            return null;
        }
        catch (RabbitMQClientException failure)
        {
            // What re-declaring the topology raises when the broker refuses it — mismatched arguments
            // on a queue somebody else declared differently, or a resource alarm.
            return failure;
        }
        catch (IOException failure)
        {
            return failure;
        }
        catch (TimeoutException failure)
        {
            // The client raises a plain TimeoutException when one of its own RPCs runs past the
            // continuation timeout, which is neither of the two above.
            return failure;
        }
    }

    /// <summary>Drops the current channel, clearing the field first so a failing close leaves no husk.</summary>
    private async ValueTask DiscardChannelAsync()
    {
        var channel = _channel;
        _channel = null;

        if (channel is not null)
            await channel.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Drops the current connection, on the same reasoning as <see cref="DiscardChannelAsync"/>.</summary>
    private async ValueTask DiscardConnectionAsync()
    {
        var connection = _connection;
        _connection = null;

        if (connection is not null)
            await connection.DisposeAsync().ConfigureAwait(false);
    }
}
