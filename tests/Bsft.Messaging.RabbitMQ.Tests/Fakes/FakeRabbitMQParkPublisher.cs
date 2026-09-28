using Bsft.Messaging.RabbitMQ.Consuming;

namespace Bsft.Messaging.RabbitMQ.Tests.Fakes;

/// <summary>Records what was parked, and can report that it could not preserve the message.</summary>
internal sealed class FakeRabbitMQParkPublisher : IRabbitMQParkPublisher
{
    public List<(RabbitMQDelivery Delivery, string Reason)> Parked { get; } = [];

    /// <summary>Whether the parked copy reaches the dead-letter exchange. Defaults to yes.</summary>
    public bool Preserves { get; set; } = true;

    /// <summary>Set to have something happen while the parked copy is being published.</summary>
    public Func<CancellationToken, Task>? Behaviour { get; set; }

    public async Task<bool> TryPublishAsync(
        RabbitMQDelivery delivery, string reason, CancellationToken ct = default)
    {
        Parked.Add((delivery, reason));

        if (Behaviour is not null)
            await Behaviour(ct);

        return Preserves;
    }
}
