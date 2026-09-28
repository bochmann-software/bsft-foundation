namespace Bsft.Messaging.EFCore.Tests.Fakes;

/// <summary>
/// Records the correlation id that was ambient while the event was handled, which is what a real
/// handler's logging scope and audit trail pick up.
/// </summary>
public sealed class CorrelationRecordingOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedEvent>
{
    private readonly IMessageCorrelationScope _scope;
    private readonly CorrelationLog _log;

    public CorrelationRecordingOrderPlacedHandler(IMessageCorrelationScope scope, CorrelationLog log)
    {
        _scope = scope;
        _log = log;
    }

    public Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        _log.Add(_scope.Current);
        return Task.CompletedTask;
    }
}
