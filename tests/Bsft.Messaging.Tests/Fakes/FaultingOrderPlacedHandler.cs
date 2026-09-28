namespace Bsft.Messaging.Tests.Fakes;

public sealed class FaultingOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedEvent>
{
    public async Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        await Task.Yield();
        throw new InvalidOperationException("handler faulted after an await");
    }
}
