using System.Threading.Channels;
using Bsft.Messaging.Consuming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Messaging.Loopback;

/// <summary>
/// An in-process transport that is both ends of the wire: published messages go into a queue, and the
/// consumer host reads them out of it again. It is the single-process deployment of the full consuming
/// pipeline, and the transport tests use in place of a broker.
/// </summary>
/// <remarks>
/// <para>
/// It is the alternative to <see cref="InMemoryMessageBus"/>, and the difference between them is the
/// point of it. The in-memory bus handles the event inside the publish, so a handler failure fails the
/// publish and the outbox retries the whole message; there is no inbox and no parking in that path.
/// The loopback hands the message to <see cref="IntegrationEventConsumerHost"/> instead, so a
/// single-process deployment gets the pipeline it would have against a broker — inbox deduplication,
/// bounded attempts, parked poison messages — and behaves the same way when a broker is put in later.
/// </para>
/// <para>
/// The queue is in memory and unbounded. A message that has been published but not yet consumed is
/// lost if the process stops, which is the one guarantee a real transport adds and this one cannot;
/// what is not lost is anything still in the outbox, because the outbox only marks a message processed
/// once this transport has taken it.
/// </para>
/// <para>
/// Being unbounded has a cost worth knowing before this is used for more than one process's worth of
/// traffic: a publish returns as soon as the message is queued, so an outbox dispatcher draining a
/// large backlog pulls all of it into memory at once instead of being held back. A broker adapter does
/// not have that problem, because a broker's own flow control is what makes the publish wait. Where
/// the backlog can outgrow the process, use one.
/// </para>
/// </remarks>
public sealed class LoopbackMessageTransport : IMessageBus, IMessageSource, ILoopbackMessageSink, IDisposable
{
    // Unbounded, deliberately. A bound would have to apply backpressure to the publisher, and the
    // consumer host puts a failed message back on this same queue from inside its single reader: with
    // a full queue that write waits for room only the blocked reader could make, and a publisher that
    // took the slot freed by the dequeue is enough to make that permanent. The queue is therefore
    // sized by whatever is fed into it, which is one more reason this is a transport for one process
    // and for tests rather than a broker — see the remarks on the class.
    private readonly Channel<LoopbackDelivery> _queue = Channel.CreateUnbounded<LoopbackDelivery>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly List<ParkedMessage> _parked = [];
    private readonly object _parkedGate = new();

    // The timers of the redeliveries that are waiting out a delay. They are held rather than started
    // and forgotten for two reasons: nothing else roots a timer, so a collected one would silently
    // never fire, and disposing the transport has to be able to stop the ones still pending.
    private readonly HashSet<ITimer> _scheduled = [];
    private readonly object _scheduledGate = new();
    private bool _disposed;

    private readonly int _parkedCapacity;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LoopbackMessageTransport> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopbackMessageTransport"/> class.
    /// </summary>
    /// <param name="options">The messaging options carrying the parked list's capacity.</param>
    /// <param name="timeProvider">Time provider used to stamp a parked message.</param>
    /// <param name="logger">Logger used to report parked messages.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="ConsumerOptions.ParkedMessageCapacity"/> is below one.
    /// </exception>
    /// <remarks>
    /// The capacity is re-checked here although the options validator already rejects it at startup:
    /// the transport can be constructed without the container, and a capacity below one would drop
    /// every message it was asked to keep — the one outcome that has nothing else holding it.
    /// </remarks>
    public LoopbackMessageTransport(
        IOptions<BsftMessagingOptions> options,
        TimeProvider timeProvider,
        ILogger<LoopbackMessageTransport> logger)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var consumer = options.Value.Consumer;

        if (consumer.ParkedMessageCapacity < 1)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                consumer.ParkedMessageCapacity,
                "The parked message capacity must be at least 1.");

        _parkedCapacity = consumer.ParkedMessageCapacity;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the messages the consumer parked, most recent last, bounded to
    /// <see cref="ConsumerOptions.ParkedMessageCapacity"/>.
    /// </summary>
    /// <remarks>
    /// This is what a transport with a dead-letter queue offers as a queue. It is here so that parking
    /// stays inspectable in the single-process case rather than being only a log line, which is what
    /// "never silently drop" has to mean when there is no broker to hold the message.
    /// </remarks>
    public IReadOnlyList<ParkedMessage> ParkedMessages
    {
        get
        {
            lock (_parkedGate)
                return _parked.ToArray();
        }
    }

    /// <inheritdoc />
    public async Task PublishAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));

        await _queue.Writer.WriteAsync(new LoopbackDelivery(this, message, 1), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<IMessageDelivery> ReceiveAsync(CancellationToken cancellationToken = default) =>
        _queue.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Ends the stream once the queue has run dry, so a consumer host draining this transport stops
    /// instead of waiting for a message that is never published. It is a test affordance: a completed
    /// transport refuses further publishes and cannot be reopened.
    /// </summary>
    public void Complete() => _queue.Writer.Complete();

    /// <inheritdoc />
    async Task ILoopbackMessageSink.RedeliverAsync(
        IntegrationEventMessage message, int deliveryAttempt, TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
        {
            ScheduleRedelivery(message, deliveryAttempt, delay);

            return;
        }

        // Appended rather than pushed back to the head, so one message that keeps failing does not
        // starve the ones behind it while it uses up its attempts.
        await _queue.Writer
            .WriteAsync(new LoopbackDelivery(this, message, deliveryAttempt), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Puts the message back on the queue once the delay has passed, without holding anything until
    /// then.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole point of the delay being a parameter rather than a sleep in the consumer. The
    /// consumer reads one message at a time, so a wait taken before the hand-back is a wait for every
    /// message behind it: one recipient a push service is throttling would hold up every notification on
    /// every channel. Here the wait belongs to the message alone, and the consumer goes straight back to
    /// the queue.
    /// </para>
    /// <para>
    /// A scheduled redelivery lives in memory like everything else in this transport, so a process that
    /// stops before the timer fires loses it — exactly as it loses whatever is still queued. What it
    /// does not lose is the outbox row behind the message, which is where a delay that has to survive a
    /// restart is recorded.
    /// </para>
    /// </remarks>
    private void ScheduleRedelivery(IntegrationEventMessage message, int deliveryAttempt, TimeSpan delay)
    {
        ITimer? timer = null;

        // Created idle and started below, so the timer is registered before it can possibly fire and the
        // callback cannot observe the variable it disposes itself through as unassigned. The suppression
        // is that guarantee written down: nothing runs the callback until Change starts it.
        timer = _timeProvider.CreateTimer(
            _ => CompleteScheduledRedelivery(message, deliveryAttempt, timer!),
            state: null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        // Registered and started under the one lock, because disposal takes the same lock: arming
        // afterwards leaves a window in which Dispose has already disposed this very timer, and
        // starting a disposed timer neither throws nor schedules anything — the message would simply
        // never come back, which is the one outcome this transport must not produce silently. Starting
        // it here cannot deadlock: the timer is idle until this call, so its callback cannot already be
        // waiting for the lock, and a callback that starts on another thread merely waits for it.
        lock (_scheduledGate)
        {
            if (_disposed)
            {
                timer.Dispose();
                LogRedeliveryDropped(message);

                return;
            }

            _scheduled.Add(timer);
            timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Hands a held-back message to the queue and retires the timer that was holding it.</summary>
    private void CompleteScheduledRedelivery(IntegrationEventMessage message, int deliveryAttempt, ITimer timer)
    {
        lock (_scheduledGate)
            _scheduled.Remove(timer);

        timer.Dispose();

        // TryWrite rather than WriteAsync: the queue is unbounded, so the only way this fails is a
        // completed transport, and a callback is no place to raise that. Losing the message is what
        // completing the transport already means, but it is said out loud rather than dropped quietly.
        if (_queue.Writer.TryWrite(new LoopbackDelivery(this, message, deliveryAttempt)))
            return;

        LogRedeliveryDropped(message);
    }

    /// <summary>Reports a held-back message that the queue would no longer take.</summary>
    private void LogRedeliveryDropped(IntegrationEventMessage message) =>
        _logger.LogWarning(
            "Integration event {EventId} of type {EventName} was held back for a later attempt, but the loopback transport was closed before it could be delivered again.",
            message.EventId,
            message.EventName);

    /// <summary>
    /// Stops the redeliveries that are still waiting out their delay. Anything already on the queue is
    /// unaffected; this transport keeps its messages in memory, so a stopped process loses them either
    /// way.
    /// </summary>
    public void Dispose()
    {
        ITimer[] pending;

        lock (_scheduledGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            pending = [.. _scheduled];
            _scheduled.Clear();
        }

        foreach (var timer in pending)
            timer.Dispose();
    }

    /// <inheritdoc />
    void ILoopbackMessageSink.Park(IntegrationEventMessage message, string reason)
    {
        _logger.LogError(
            "Integration event {EventId} of type {EventName} was parked by the loopback transport: {ParkReason}",
            message.EventId,
            message.EventName,
            reason);

        lock (_parkedGate)
        {
            _parked.Add(new ParkedMessage(message, reason, _timeProvider.GetUtcNow()));

            if (_parked.Count > _parkedCapacity)
                _parked.RemoveAt(0);
        }
    }
}
