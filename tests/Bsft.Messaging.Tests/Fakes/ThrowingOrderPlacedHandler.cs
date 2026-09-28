namespace Bsft.Messaging.Tests.Fakes;

public sealed class ThrowingOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedEvent>
{
    public Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default) =>
        throw new InvalidOperationException("handler failed");
}
