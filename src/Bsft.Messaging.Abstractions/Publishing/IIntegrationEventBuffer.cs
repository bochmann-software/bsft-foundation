namespace Bsft.Messaging.Publishing;

/// <summary>
/// The reading side of the staging area that <see cref="IIntegrationEventCollector"/> fills. It is a
/// separate contract on purpose: application code stages events and has no business draining them,
/// while the persistence layer drains them and has no business staging any.
/// </summary>
public interface IIntegrationEventBuffer
{
    /// <summary>
    /// Removes the staged events from the buffer and returns them in the order they were staged.
    /// </summary>
    /// <returns>The events staged since the previous call; empty when nothing was staged.</returns>
    IReadOnlyList<IIntegrationEvent> TakePending();
}
