using Bsft.EventAggregator.Saga;

namespace Bsft.Mediator.EventAggregator.Tests.Support;

/// <summary>A saga started by the mediator notification, which only works if forwarding works.</summary>
public class OrderSagaFlow : ISagaFlow
{
    public void Configure(ISagaBuilderFactory factory)
    {
        if (factory is null)
            throw new ArgumentNullException(nameof(factory));

        factory.WithState<OrderSagaState>()
            .StartWith<OrderCreated>(e => e.OrderId)
            .Then((ctx, e) =>
            {
                ctx.State.OrderId = e.OrderId;
                ctx.GetService<DeliveryTrace>().Add("saga-step");
                return Task.CompletedTask;
            })
            .Build();
    }
}
