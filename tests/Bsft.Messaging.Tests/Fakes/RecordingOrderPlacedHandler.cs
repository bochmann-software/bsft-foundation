namespace Bsft.Messaging.Tests.Fakes;

public sealed class RecordingOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedEvent>
{
    private readonly List<OrderPlacedEvent> _handled = [];

    public IReadOnlyList<OrderPlacedEvent> Handled => _handled;

    public Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        _handled.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
