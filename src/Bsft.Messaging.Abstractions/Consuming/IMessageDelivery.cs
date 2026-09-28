namespace Bsft.Messaging.Consuming;

/// <summary>
/// One message handed to the consumer host by an <see cref="IMessageSource"/>, together with the three
/// ways the host can finish with it. Exactly one of the completion methods is called for every
/// delivery, and the transport maps it onto whatever its own protocol calls the same thing.
/// </summary>
/// <remarks>
/// <para>
/// The three outcomes mirror the producing side one for one, which is what keeps the two halves of the
/// pipeline explainable together: <see cref="AcknowledgeAsync"/> is the consuming counterpart of
/// marking an outbox row processed, <see cref="RetryAsync"/> of abandoning its lease, and
/// <see cref="ParkAsync"/> of retiring it to the dead-letter state.
/// </para>
/// <para>
/// An implementation is used by one consumer host at a time and its completion methods are called from
/// the host's own loop, so it does not have to be thread-safe against itself. It must be safe to call a
/// completion method after the host has begun shutting down: the host completes the delivery it is
/// holding before it unwinds.
/// </para>
/// </remarks>
public interface IMessageDelivery
{
    /// <summary>Gets the message that was delivered.</summary>
    IntegrationEventMessage Message { get; }

    /// <summary>
    /// Gets the one-based number of this delivery attempt: 1 the first time the message is delivered,
    /// 2 for its first redelivery, and so on.
    /// </summary>
    /// <remarks>
    /// It is what the consumer host bounds its retries by, so a transport that cannot count
    /// redeliveries — and several cannot, or only after the queue has been declared for it — reports 1
    /// every time. Such a transport must bound redelivery itself, because a host that is told every
    /// delivery is the first one will retry a poison message forever. For AMQP that means a quorum
    /// queue with a delivery limit, whose <c>x-delivery-count</c> header this property is then read
    /// from; for a transport with neither, parking after the transport's own limit is the honest
    /// substitute.
    /// </remarks>
    int DeliveryAttempt { get; }

    /// <summary>
    /// Accepts the message: it has been handled, and the transport must not deliver it again.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the transport has recorded the acknowledgement.</returns>
    Task AcknowledgeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the message for another attempt: handling it failed in a way that may not fail again.
    /// The transport redelivers it, to this consumer or to another instance of it, and not before
    /// <paramref name="delay"/> has passed.
    /// </summary>
    /// <param name="delay">
    /// How long the message must be held back before it is delivered again. <see cref="TimeSpan.Zero"/>
    /// — and any non-positive value — means "as soon as possible", which is what the consumer passes
    /// when neither the failure nor the configuration asked for a wait.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the transport has taken the message back.</returns>
    /// <remarks>
    /// <para>
    /// The delay is a promise about the redelivery and not about this call: an implementation returns as
    /// promptly as it can, because the consumer host is holding its one message while it waits here, and
    /// a wait taken in this method is a wait for every message behind it. Scheduling the redelivery
    /// instead — a timer, a delayed queue, a broker's own delay topology — is what the contract asks
    /// for, and it is the only shape in which a long wait costs nothing.
    /// </para>
    /// <para>
    /// A transport with no way to schedule anything has two honest answers, and must document which one
    /// it gives: wait here, paying the stall the consumer used to pay, or redeliver at once and say that
    /// the delay is not obeyed. Silently ignoring it is what the parameter exists to stop, because the
    /// caller has no other way to find out.
    /// </para>
    /// </remarks>
    Task RetryAsync(TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the message aside without handling it and without redelivering it, recording why.
    /// </summary>
    /// <param name="reason">
    /// Why the message was parked, in a form an operator can act on. It is derived from an exception or
    /// from the message's own metadata, so it can quote parts of the event; a transport has to treat it
    /// as at least as sensitive as the payload.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the transport has parked the message.</returns>
    /// <remarks>
    /// A parked message must stay inspectable — a dead-letter queue where the transport has one, an
    /// equivalent record where it does not. Parking is the one outcome that ends a message's life
    /// without anyone having handled it, so a transport that implements it by discarding the message
    /// turns the host's loudest failure path into a silent drop, which is precisely what the host parks
    /// to avoid.
    /// </remarks>
    Task ParkAsync(string reason, CancellationToken cancellationToken = default);
}
