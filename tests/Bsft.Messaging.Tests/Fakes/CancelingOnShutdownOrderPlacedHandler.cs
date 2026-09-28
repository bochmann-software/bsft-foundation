namespace Bsft.Messaging.Tests.Fakes;

/// <summary>
/// Ends on a token of its own, but only once the token the publish was started with has been
/// cancelled: the caller withdrew the publish, so this is a shutdown and not a failed delivery even
/// though the exception carries a foreign token.
/// </summary>
public sealed class CancelingOnShutdownOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedEvent>
{
    public async Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            using var inner = new CancellationTokenSource();
            await inner.CancelAsync().ConfigureAwait(false);

            throw new TaskCanceledException("The handler gave up while the host was shutting down.", null, inner.Token);
        }
    }
}
