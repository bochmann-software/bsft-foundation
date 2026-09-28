using Bsft.Messaging.Consuming;

namespace Bsft.Messaging.Tests.Fakes;

/// <summary>Counts what the host handed it, and can fail or block on demand.</summary>
public sealed class FakeIntegrationEventConsumer : IIntegrationEventConsumer
{
    private readonly List<IMessageDelivery> _consumed = [];

    public IReadOnlyList<IMessageDelivery> Consumed => _consumed;

    public Exception? Failure { get; set; }

    public Task ConsumeAsync(IMessageDelivery delivery, CancellationToken ct = default)
    {
        _consumed.Add(delivery);

        return Failure is not null ? Task.FromException(Failure) : Task.CompletedTask;
    }
}
