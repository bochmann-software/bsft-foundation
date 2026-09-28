namespace Bsft.Messaging.RabbitMQ.Tests.Fakes;

/// <summary>The one event the end-to-end binding tests carry through the pipeline.</summary>
internal sealed record SeamOrderPlacedEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTimeOffset OccurredAt { get; init; }

    public string OrderNumber { get; init; } = string.Empty;
}
