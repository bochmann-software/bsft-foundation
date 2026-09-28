namespace Bsft.Messaging.Publishing;

/// <summary>
/// Collects the integration events raised while a unit of work is in flight. Application code stages
/// its events here instead of publishing them directly, so nothing leaves the process before the
/// state change they describe has been committed.
/// </summary>
/// <remarks>
/// Staging is not delivery. The persistence layer picks the staged events up while it writes the unit
/// of work and turns them into outbox rows within the same transaction.
/// </remarks>
public interface IIntegrationEventCollector
{
    /// <summary>
    /// Stages an integration event for the current unit of work.
    /// </summary>
    /// <param name="integrationEvent">The event to stage.</param>
    void Enqueue(IIntegrationEvent integrationEvent);
}
