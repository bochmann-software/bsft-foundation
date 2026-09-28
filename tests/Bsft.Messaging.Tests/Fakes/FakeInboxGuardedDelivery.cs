using Bsft.Messaging.Inbox;

namespace Bsft.Messaging.Tests.Fakes;

/// <summary>Stands in for the inbox-guarded delivery so the consumer's decisions can be tested alone.</summary>
public sealed class FakeInboxGuardedDelivery : IInboxGuardedDelivery
{
    public EventDeliveryOutcome Outcome { get; set; } = EventDeliveryOutcome.Handled;

    public Exception? Failure { get; set; }

    /// <summary>Set to end the delivery on a token of its own, as a handler timeout does.</summary>
    public bool CancelsItself { get; set; }

    /// <summary>Set to block until the caller's token is cancelled, as a shutdown mid-delivery does.</summary>
    public bool BlocksUntilCancelled { get; set; }

    public int Deliveries { get; private set; }

    public async Task<EventDeliveryOutcome> DeliverAsync(
        IntegrationEventMessage message, IIntegrationEvent integrationEvent, CancellationToken ct = default)
    {
        Deliveries++;

        if (Failure is not null)
            throw Failure;

        if (CancelsItself)
        {
            using var ownToken = new CancellationTokenSource();
            await ownToken.CancelAsync().ConfigureAwait(false);
            ownToken.Token.ThrowIfCancellationRequested();
        }

        if (BlocksUntilCancelled)
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);

        return Outcome;
    }
}
