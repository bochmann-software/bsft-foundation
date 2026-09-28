namespace Bsft.Messaging.Loopback;

/// <summary>
/// What a <see cref="Loopback.LoopbackDelivery"/> can do to the transport that produced it. It exists so the
/// delivery depends on a contract rather than on <see cref="Loopback.LoopbackMessageTransport"/> itself, which
/// would make the two types mutually dependent.
/// </summary>
internal interface ILoopbackMessageSink
{
    /// <summary>
    /// Puts a message back at the end of the queue for another attempt, either at once or once
    /// <paramref name="delay"/> has passed.
    /// </summary>
    /// <param name="message">The message to deliver again.</param>
    /// <param name="deliveryAttempt">The one-based number the next delivery of it will report.</param>
    /// <param name="delay">
    /// How long the message is held back first. Non-positive queues it immediately; anything else is
    /// scheduled, so the call returns before the message is back on the queue.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// A task that completes once the message is queued again, or — for a scheduled redelivery — once
    /// the schedule has been set up.
    /// </returns>
    Task RedeliverAsync(
        IntegrationEventMessage message, int deliveryAttempt, TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a message aside where it stays visible instead of being delivered again.
    /// </summary>
    /// <param name="message">The message to park.</param>
    /// <param name="reason">Why it was parked.</param>
    void Park(IntegrationEventMessage message, string reason);
}
