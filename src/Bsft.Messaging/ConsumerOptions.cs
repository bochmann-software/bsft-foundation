namespace Bsft.Messaging;

/// <summary>
/// Options controlling the consuming side: which consumer the inbox records deliveries for, how often
/// a message may be retried, and how long the host waits before it re-subscribes to its source.
/// </summary>
public sealed class ConsumerOptions
{
    /// <summary>
    /// Gets or sets the identity this consumer records its processed events under. It has no default,
    /// and the host refuses to start without it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is half of the inbox key, so it decides which deliveries count as the same delivery. Two
    /// services consuming the same event are independent and must use different names — sharing one
    /// would make whichever of them ran first swallow the event for the other. Two instances of the
    /// <em>same</em> service must use the same name, because between them the event genuinely is one
    /// delivery.
    /// </para>
    /// <para>
    /// There is deliberately no default derived from the assembly or the host name. Either would make
    /// a rename or a scale-out silently change which events are considered already processed, and the
    /// symptom of that — a backlog handled a second time, or an event nobody handles — appears long
    /// after the change that caused it. Choose a stable name and treat it like a database name.
    /// </para>
    /// </remarks>
    public string ConsumerName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how often a message may be delivered before it is parked. Defaults to 10, matching
    /// <see cref="OutboxOptions.MaxDeliveryAttempts"/> on the producing side.
    /// </summary>
    /// <remarks>
    /// It is counted against <see cref="Bsft.Messaging.Consuming.IMessageDelivery.DeliveryAttempt"/>, so it only
    /// bounds anything where the transport counts redeliveries. Against one that cannot, every
    /// delivery claims to be the first and this limit is never reached; such a transport has to bound
    /// redelivery itself.
    /// </remarks>
    public int MaxDeliveryAttempts { get; set; } = 10;

    /// <summary>
    /// Gets or sets how long the host waits before it enumerates its message source again after the
    /// stream ended or failed. Defaults to five seconds.
    /// </summary>
    /// <remarks>
    /// It has to be positive: a source whose stream ends immediately — a broker that is refusing
    /// connections, typically — would otherwise be re-subscribed to in a tight loop.
    /// </remarks>
    public TimeSpan ReceiveRestartDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets how many completed deliveries the in-memory inbox ledger keeps before it drops the
    /// oldest record. Defaults to 100 000. It has to be positive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It bounds <see cref="Inbox.InMemoryInboxLedger"/> only; the EF Core store keeps its record until
    /// <see cref="InboxRetention"/> expires it and ignores this. The default costs single-digit megabytes and is
    /// deliberately far larger than any redelivery window a broker or a retry policy produces: a record
    /// is only useful until the last redelivery of its event has been refused, and an event a hundred
    /// thousand deliveries ago is not coming back.
    /// </para>
    /// <para>
    /// Shrinking it narrows the duplicate-detection window to that many recent deliveries, so an event
    /// redelivered after more than that have been recorded reaches the handlers a second time. Raise it
    /// where memory is cheaper than a duplicate; a deployment that needs deduplication to survive a
    /// restart at all wants the EF Core store rather than a larger number here.
    /// </para>
    /// </remarks>
    public int InboxLedgerCapacity { get; set; } = 100_000;

    /// <summary>
    /// Gets or sets how many parked messages <see cref="Loopback.LoopbackMessageTransport"/> keeps for
    /// inspection before it drops the oldest. Defaults to 100. It has to be positive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It bounds the loopback transport only. A broker adapter parks into the broker's own dead-letter
    /// queue, which is sized there and ignores this.
    /// </para>
    /// <para>
    /// Parking is meant to be rare and the list lives for as long as the process does, so it is bounded
    /// rather than allowed to grow: a misrouted stream of messages would otherwise be a memory leak with
    /// a diagnostic excuse. The default is sized for reading, not for storage — a hundred parked
    /// messages is already far more than anyone reads one by one, and a deployment that parks enough for
    /// the bound to matter has a problem the list is not the answer to. Raise it where a burst of
    /// parking has to stay inspectable in full; the cost is the whole message being held in memory.
    /// </para>
    /// </remarks>
    public int ParkedMessageCapacity { get; set; } = 100;

    /// <summary>
    /// Gets or sets the first interval a failed message is held back for before the transport is asked
    /// to deliver it again. It doubles with every further attempt, up to <see cref="MaxRetryDelay"/>.
    /// Defaults to <see cref="TimeSpan.Zero"/>, which hands the message back at once. It must not be
    /// negative.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is spent on the retry path only. A message that was handled, that the inbox recognised as a
    /// redelivery, or that is parked is completed without any pause, so nothing waits but the message
    /// that actually failed.
    /// </para>
    /// <para>
    /// The default preserves the behaviour of every earlier release rather than being the recommended
    /// value: without it a deterministically failing message burns all
    /// <see cref="MaxDeliveryAttempts"/> in milliseconds and is parked before whatever it was waiting
    /// for — a database that is failing over, a downstream service restarting — can possibly have come
    /// back. Five seconds, matching <see cref="ReceiveRestartDelay"/>, is the value to reach for; with
    /// the doubling it turns the default ten attempts into far more patience than the flat delay of
    /// earlier releases bought.
    /// </para>
    /// <para>
    /// Know what a wait can cost before raising it. Since 1.3.6 the consumer no longer sleeps itself: it
    /// passes the interval to the transport, which decides how to spend it. The loopback schedules the
    /// redelivery and holds nothing, so a waiting message no longer delays the ones behind it; the
    /// RabbitMQ transport has no delayed requeue and waits before it nacks, which does hold the consumer
    /// exactly as earlier releases did. <see cref="MaxRetryDelay"/> is the bound on that, and a long
    /// wait against a broker belongs in its own retry topology rather than here.
    /// </para>
    /// <para>
    /// A wait a handler asks for through <see cref="Core.Retry.IRetryAfter"/> is honoured as a floor on
    /// top of this schedule, so a channel that was told to slow down is obeyed even where this is left
    /// at zero.
    /// </para>
    /// </remarks>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Gets or sets the ceiling of the consumer's retry schedule. Defaults to thirty seconds. It caps
    /// the doubling of <see cref="RetryDelay"/> and clamps what a failure asks for. It has to be
    /// positive.
    /// </summary>
    /// <remarks>
    /// It is much shorter than <see cref="OutboxOptions.MaxRetryDelay"/> on the producing side, because
    /// the two sides pay differently: holding an outbox row back costs the delay and nothing else, while
    /// a transport that cannot schedule a redelivery pays for the wait with the whole consumer. Thirty
    /// seconds is what a stalled consumer can afford; where waits have to be longer than that, the
    /// transport needs a delay topology and not a larger number here.
    /// </remarks>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long the EF Core inbox keeps the record of a completed delivery.
    /// <see langword="null"/>, the default, keeps it forever. It has to be positive when set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It bounds the EF Core inbox only; the in-memory ledger is bounded by
    /// <see cref="InboxLedgerCapacity"/> instead and ignores this.
    /// </para>
    /// <para>
    /// Setting it makes the retention <em>the duplicate-detection window</em>, and that is the whole of
    /// the trade: a redelivery that arrives more than this long after the delivery it repeats finds no
    /// record of it and reaches the handlers a second time. Size it against the longest redelivery the
    /// deployment can produce — a broker's own retry topology, a message parked and replayed by hand, a
    /// backlog drained after a long outage — and not against what the table costs to keep.
    /// </para>
    /// <para>
    /// The default is <see langword="null"/> because no default can know that window. Keeping every row
    /// forever is a table that grows with throughput, which is a problem an operator notices; a window
    /// that quietly turned out to be too short is a duplicate nobody notices until it has been paid for.
    /// </para>
    /// </remarks>
    public TimeSpan? InboxRetention { get; set; }
}
