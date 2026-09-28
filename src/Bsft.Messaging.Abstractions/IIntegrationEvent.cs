namespace Bsft.Messaging;

/// <summary>
/// Contract for an event that leaves the process it was raised in. Integration events are a
/// deliberately separate concept from the in-process domain events of the event aggregator: they are
/// serialized, stored in the outbox and delivered to other services, so their shape is a published
/// contract that has to stay backwards compatible, while a domain event is free to change with the
/// code that raises it.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>
    /// Gets the identity of this event. It travels with the message, so a consumer that receives the
    /// same event twice — which at-least-once delivery permits — can recognise the repeat.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// Gets the UTC instant at which the event occurred. This is the instant of the state change that
    /// raised it, not the instant it was published.
    /// </summary>
    DateTimeOffset OccurredAt { get; }
}
