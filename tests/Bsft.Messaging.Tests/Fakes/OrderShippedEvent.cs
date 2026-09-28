namespace Bsft.Messaging.Tests.Fakes;

public record OrderShippedEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTimeOffset OccurredAt { get; init; }
}
