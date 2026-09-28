using Bsft.EventAggregator.Saga;

namespace Bsft.Mediator.EventAggregator.Tests.Support;

/// <summary>A local mediator handler, to prove forwarding does not replace local dispatch.</summary>
public class OrderCreatedNotificationHandler : INotificationHandler<OrderCreated>
{
    private readonly DeliveryTrace _trace;

    public OrderCreatedNotificationHandler(DeliveryTrace trace)
    {
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
    }

    public Task HandleAsync(OrderCreated notification, CancellationToken ct = default)
    {
        _trace.Add("local-handler");
        return Task.CompletedTask;
    }
}
