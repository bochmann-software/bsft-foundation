namespace Bsft.Messaging.EFCore.Tests.Fakes;

public record OrderShippedEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTimeOffset OccurredAt { get; init; }
}
