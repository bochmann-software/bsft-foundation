namespace Bsft.Messaging.RabbitMQ.Tests.Fakes;

/// <summary>The typed handler the binding has to reach.</summary>
internal sealed class SeamOrderPlacedHandler : IIntegrationEventHandler<SeamOrderPlacedEvent>
{
    private readonly SeamHandlerLog _log;

    public SeamOrderPlacedHandler(SeamHandlerLog log) => _log = log;

    public Task HandleAsync(SeamOrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        _log.Add(integrationEvent.OrderNumber);

        return _log.Fails
            ? Task.FromException(new InvalidOperationException("the downstream system refused the order"))
            : Task.CompletedTask;
    }
}
