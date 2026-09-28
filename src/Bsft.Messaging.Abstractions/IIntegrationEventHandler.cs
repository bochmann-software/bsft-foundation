namespace Bsft.Messaging;

/// <summary>
/// Handles an integration event of a specific type. Delivery is at-least-once, so a handler must be
/// idempotent: it can be invoked more than once for the same <see cref="IIntegrationEvent.EventId"/>.
/// </summary>
/// <typeparam name="TEvent">The integration event type this handler consumes.</typeparam>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    /// <summary>
    /// Handles the specified integration event.
    /// </summary>
    /// <param name="integrationEvent">The event to handle.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the event has been handled.</returns>
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}
