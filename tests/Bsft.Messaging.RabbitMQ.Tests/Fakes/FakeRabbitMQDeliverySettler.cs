using Bsft.Messaging.RabbitMQ.Consuming;

namespace Bsft.Messaging.RabbitMQ.Tests.Fakes;

/// <summary>Records which settlement was asked for, and which channel it was asked for.</summary>
internal sealed class FakeRabbitMQDeliverySettler : IRabbitMQDeliverySettler
{
    public List<(string Outcome, IRabbitMQChannel Channel, RabbitMQDelivery Delivery)> Settlements { get; } = [];

    public List<string> ParkReasons { get; } = [];

    public Task AcknowledgeAsync(
        IRabbitMQChannel channel, RabbitMQDelivery delivery, CancellationToken ct = default)
    {
        Settlements.Add(("acknowledged", channel, delivery));
        return Task.CompletedTask;
    }

    public Task RequeueAsync(IRabbitMQChannel channel, RabbitMQDelivery delivery, CancellationToken ct = default)
    {
        Settlements.Add(("requeued", channel, delivery));
        return Task.CompletedTask;
    }

    public Task ParkAsync(
        IRabbitMQChannel channel, RabbitMQDelivery delivery, string reason, CancellationToken ct = default)
    {
        Settlements.Add(("parked", channel, delivery));
        ParkReasons.Add(reason);

        return Task.CompletedTask;
    }
}
