namespace Bsft.Messaging;

/// <summary>
/// Transport contract for integration events. The framework ships an in-process default; a broker
/// adapter (RabbitMQ, Azure Service Bus, …) implements the same contract in its own package, so the
/// outbox dispatcher and the application code stay unchanged when the transport is swapped.
/// </summary>
/// <remarks>
/// <para>
/// A bus implementation publishes; it does not decide when an event becomes visible. That decision
/// belongs to the outbox, which only hands an event to the bus after the state change it belongs to
/// has been committed.
/// </para>
/// <para>
/// What crosses this seam is the <see cref="IntegrationEventMessage"/> rather than the typed event, because
/// the payload it carries is the encoded event the outbox already committed. A typed contract would
/// force a broker adapter to encode the event a second time, which risks putting bytes on the wire
/// that differ from the ones the transaction agreed to, and it would leave the adapter unable to
/// forward an event whose CLR type it does not reference at all.
/// </para>
/// </remarks>
public interface IMessageBus
{
    /// <summary>
    /// Publishes the specified message to its subscribers.
    /// </summary>
    /// <param name="message">The message to publish.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the message has been handed to every subscriber.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is null.</exception>
    /// <exception cref="Publishing.MessagePublishException">Thrown when at least one subscriber failed.</exception>
    Task PublishAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default);
}
