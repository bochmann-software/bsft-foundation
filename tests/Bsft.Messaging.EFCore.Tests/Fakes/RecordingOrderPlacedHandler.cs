namespace Bsft.Messaging.EFCore.Tests.Fakes;

public sealed class RecordingOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedEvent>
{
    private readonly DeliveryLog _log;

    public RecordingOrderPlacedHandler(DeliveryLog log) => _log = log;

    public Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        _log.Add(integrationEvent.OrderNumber);
        return Task.CompletedTask;
    }
}
