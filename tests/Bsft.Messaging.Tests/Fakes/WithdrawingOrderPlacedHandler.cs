namespace Bsft.Messaging.Tests.Fakes;

/// <summary>Withdraws the delivery from inside a handler, the way a host shutting down mid-run does.</summary>
public sealed class WithdrawingOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedEvent>
{
    private readonly CancellationTokenSource _delivery;

    public WithdrawingOrderPlacedHandler(CancellationTokenSource delivery) => _delivery = delivery;

    public async Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default) =>
        await _delivery.CancelAsync().ConfigureAwait(false);
}
