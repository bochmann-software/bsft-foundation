namespace Bsft.Messaging.EFCore.Tests.Fakes;

/// <summary>Fails the first delivery and succeeds from the redelivery on, which is what a transient
/// handler failure looks like and what the inbox has to leave retryable.</summary>
public sealed class FailingOnceOrderWriter : IIntegrationEventHandler<OrderPlacedEvent>
{
    private readonly HandlerAttempts _attempts;

    public FailingOnceOrderWriter(HandlerAttempts attempts) => _attempts = attempts;

    public Task HandleAsync(OrderPlacedEvent integrationEvent, CancellationToken ct = default)
    {
        if (_attempts.Next() == 1)
            throw new InvalidOperationException("The second handler fails after the first one saved.");

        return Task.CompletedTask;
    }
}
