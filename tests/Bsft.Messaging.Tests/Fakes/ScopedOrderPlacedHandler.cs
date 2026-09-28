namespace Bsft.Messaging.Tests.Fakes;

public sealed class ScopedOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedEvent>
{
    private readonly DeliveryLog _log;

    public ScopedOrderPlacedHandler(DeliveryLog log) => _log = log;

    public Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        _log.Add(integrationEvent.OrderNumber);
        return Task.CompletedTask;
    }
}
