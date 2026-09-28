using Bsft.Messaging.RabbitMQ.Consuming;

namespace Bsft.Messaging.RabbitMQ.Tests.Fakes;

/// <summary>Stands in for the message source on the other side of the subscription.</summary>
internal sealed class FakeRabbitMQDeliverySink : IRabbitMQDeliverySink
{
    public List<(IRabbitMQChannel Channel, RabbitMQDelivery Delivery)> Received { get; } = [];

    /// <summary>Set to have taking a delivery block or fail, as a full or ended stream would.</summary>
    public Func<CancellationToken, Task>? Behaviour { get; set; }

    /// <summary>What the consumer reported as the end of the stream, in the order it reported it.</summary>
    public List<Exception> Faults { get; } = [];

    public async Task TakeAsync(
        IRabbitMQChannel channel, RabbitMQDelivery delivery, CancellationToken ct = default)
    {
        Received.Add((channel, delivery));

        if (Behaviour is not null)
            await Behaviour(ct);
    }

    public void Fault(Exception failure) => Faults.Add(failure);
}
