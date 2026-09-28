namespace Bsft.Messaging.Tests.Fakes;

/// <summary>
/// Models the common shape of a handler failure that arrives as cancellation: an HttpClient whose own
/// timeout elapsed throws a <see cref="TaskCanceledException"/> carrying that timeout's token, not the
/// token the publish was started with.
/// </summary>
public sealed class TimingOutOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedEvent>
{
    public async Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        await Task.Yield();

        using var timeout = new CancellationTokenSource();
        await timeout.CancelAsync();

        throw new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout elapsing.",
            new TimeoutException(),
            timeout.Token);
    }
}
