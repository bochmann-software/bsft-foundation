namespace Bsft.Messaging.Inbox;

/// <summary>
/// Delivers a decoded event to this process's handlers with the inbox in front of them: a redelivery
/// of an event this consumer has already finished with never reaches a handler.
/// </summary>
/// <remarks>
/// This is what makes idempotency structural rather than a rule every handler has to remember. A
/// handler still has to tolerate being called twice — the window at the end of a delivery is real and
/// is described on <see cref="IInboxStore"/> — but it no longer has to recognise a redelivery of a
/// message the consumer completed hours ago, which is the case handler-side deduplication gets wrong.
/// </remarks>
public interface IInboxGuardedDelivery
{
    /// <summary>
    /// Runs the event through a fresh service scope: the message's correlation id is restored, the
    /// inbox is consulted, the handlers run, and the inbox mark is committed with whatever they wrote.
    /// </summary>
    /// <param name="message">The message the event arrived in, for its identity and correlation id.</param>
    /// <param name="integrationEvent">The decoded event to hand to the handlers.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// <see cref="EventDeliveryOutcome.Handled"/> when the handlers ran and succeeded, or
    /// <see cref="EventDeliveryOutcome.Duplicate"/> when the inbox refused the event as a redelivery.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="Publishing.MessagePublishException">Thrown when at least one handler failed.</exception>
    Task<EventDeliveryOutcome> DeliverAsync(
        IntegrationEventMessage message, IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
