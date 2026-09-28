namespace Bsft.Messaging.Tests.Fakes;

public sealed class BlockingOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedEvent>
{
    public Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default) =>
        Task.Delay(Timeout.InfiniteTimeSpan, ct);
}
