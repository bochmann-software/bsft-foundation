namespace Bsft.Messaging.EFCore.Tests.Fakes;

public record OrderPlacedEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTimeOffset OccurredAt { get; init; }

    public string OrderNumber { get; init; } = string.Empty;
}
