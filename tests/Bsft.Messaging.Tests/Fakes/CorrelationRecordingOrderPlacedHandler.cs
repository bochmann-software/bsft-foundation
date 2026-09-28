namespace Bsft.Messaging.Tests.Fakes;

/// <summary>
/// Records the correlation id that was ambient while it ran, which is what a real handler's logging
/// scope, audit trail and outgoing HTTP calls pick up.
/// </summary>
public sealed class CorrelationRecordingOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedEvent>
{
    private readonly IMessageCorrelationScope _scope;

    public CorrelationRecordingOrderPlacedHandler(IMessageCorrelationScope scope) => _scope = scope;

    public string? ObservedCorrelationId { get; private set; }

    public async Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        await Task.Yield();
        ObservedCorrelationId = _scope.Current;
    }
}
