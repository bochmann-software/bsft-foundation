namespace Bsft.Messaging;

/// <summary>
/// Options controlling the <see cref="Outbox.OutboxDispatcher"/>.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>
    /// Gets or sets the delay between polls when the outbox is empty. Defaults to five seconds. It has
    /// to be positive: without a pause an idle dispatcher would query the store in a tight loop.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum number of messages claimed per poll. Defaults to 100. The whole batch
    /// is leased until one instant and published one message at a time, so this value and
    /// <see cref="LeaseDuration"/> together fix the time a single publish may take — see the sizing
    /// rule there.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets how long a claim on a message holds. Defaults to one minute. A dispatcher that
    /// dies mid-delivery blocks its messages for at most this long; set it above the time the slowest
    /// handler needs, or another instance will deliver the same message while the first is still busy.
    /// </summary>
    /// <remarks>
    /// It also has to cover a whole batch, not one message. A poll leases every claimed message until
    /// the same instant and then publishes them sequentially, which leaves each publish an average of
    /// <see cref="LeaseDuration"/> divided by <see cref="BatchSize"/> — six hundred milliseconds at the
    /// defaults. Against an in-process bus that is ample; against a broker that waits for a publisher
    /// confirm it need not be. Size the pair so that the slowest publish times the batch size stays
    /// below the lease: raise the lease, or lower the batch size, whichever suits the deployment.
    /// <para>
    /// Getting it wrong costs throughput rather than correctness. The dispatcher stops a pass once the
    /// batch lease has run out instead of publishing under a lease it no longer holds, so the untouched
    /// remainder is simply claimed again by the next poll, and it logs a warning naming both options.
    /// The combination is deliberately not validated: the publish duration that would decide the
    /// threshold is a property of the transport and its broker, which this package cannot see, and any
    /// fixed limit would refuse configurations that are entirely sound on a fast bus.
    /// </para>
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets how often a message may fail before it is retired to the dead-letter state.
    /// Defaults to 10. Without a limit, one message that can never be delivered would be retried on
    /// every poll for the lifetime of the application. The retired row is kept, so nothing is lost.
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 10;

    /// <summary>
    /// Gets or sets the first interval a failed message is held back for. Defaults to five seconds and
    /// doubles with every further failure, up to <see cref="MaxRetryDelay"/>. It must not be negative;
    /// <see cref="TimeSpan.Zero"/> retries on the next poll, which is what every release before 1.3.6
    /// did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wait is recorded on the row, so it costs nothing: the message is simply not handed out again
    /// until it has passed, and the dispatcher spends the interval draining the rest of the outbox. That
    /// is the whole difference to the consuming side, where a wait may hold a transport.
    /// </para>
    /// <para>
    /// It exists because a fixed retry spends the attempt budget faster than anything it waits for can
    /// recover. At the previous behaviour — retry on every poll — the default ten attempts were used up
    /// in under a minute, so a downstream service restarting or a database failing over dead-lettered
    /// every message in flight. These defaults turn the same ten attempts into roughly twenty minutes.
    /// </para>
    /// <para>
    /// A wait a handler asks for through <see cref="Core.Retry.IRetryAfter"/> is honoured as a floor on
    /// top of this schedule, so a service that says how long it wants to be left alone is obeyed rather
    /// than retried on the framework's own rhythm.
    /// </para>
    /// </remarks>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the ceiling of the retry schedule. Defaults to five minutes. It caps the doubling of
    /// <see cref="RetryBackoff"/> and clamps what a failure may ask for, so it answers "how long can one
    /// message be held back" on its own. It has to be positive.
    /// </summary>
    /// <remarks>
    /// It is generous compared with its counterpart on the consuming side, and deliberately so: holding
    /// a row back costs nothing but the delay itself, whereas a transport that cannot schedule a
    /// redelivery pays for the consumer's wait with every message behind it.
    /// </remarks>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
}
