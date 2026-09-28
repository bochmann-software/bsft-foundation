using Bsft.Messaging.Publishing;

namespace Bsft.Messaging.Tests.Fakes;

public sealed class FakeMessageBus : IMessageBus
{
    public List<IntegrationEventMessage> Published { get; } = [];

    public Exception? PublishFailure { get; set; }

    /// <summary>
    /// Fails the publish of these event names only, the way a transport that cannot route a name does.
    /// </summary>
    public HashSet<string> FailingEventNames { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Ends every publish as cancelled on a token of the bus's own, the way a handler whose HTTP call
    /// timed out does. The token the publish was started with stays uncancelled.
    /// </summary>
    public bool PublishTimesOut { get; set; }

    /// <summary>
    /// Runs before the publish is recorded, so a test can let time pass the way a broker that waits for
    /// a publisher confirm does.
    /// </summary>
    public Action<IntegrationEventMessage>? OnPublish { get; set; }

    public Task PublishAsync(IntegrationEventMessage message, CancellationToken ct = default)
    {
        OnPublish?.Invoke(message);

        if (PublishTimesOut)
        {
            using var timeout = new CancellationTokenSource();
            timeout.Cancel();

            return Task.FromCanceled(timeout.Token);
        }

        if (PublishFailure is not null)
            return Task.FromException(PublishFailure);

        if (FailingEventNames.Contains(message.EventName))
            return Task.FromException(
                new MessagePublishException($"nothing consumes '{message.EventName}'"));

        Published.Add(message);

        return Task.CompletedTask;
    }
}
