using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests;

public class SagaFlowTests
{
    public record OrderPlaced(string OrderId, string CustomerId);
    public record PaymentReceived(string OrderId, decimal Amount);
    public record OrderShipped(string OrderId);

    public class OrderSagaState
    {
        public string CustomerId { get; set; } = string.Empty;
        public decimal AmountPaid { get; set; }
        public bool Shipped { get; set; }
    }

    public class OrderSagaFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderSagaState>()
                .StartWith<OrderPlaced>(e => e.OrderId)
                .Then(async (ctx, e) =>
                {
                    ctx.State.CustomerId = e.CustomerId;
                    await Task.CompletedTask;
                })
                .WaitFor<PaymentReceived>(e => e.OrderId)
                .Then(async (ctx, e) =>
                {
                    ctx.State.AmountPaid = e.Amount;
                    await Task.CompletedTask;
                })
                .WaitFor<OrderShipped>(e => e.OrderId)
                .Then(async (ctx, e) =>
                {
                    ctx.State.Shipped = true;
                    await Task.CompletedTask;
                })
                .Complete();
        }
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        services.AddBsftSagaFlow<OrderSagaFlow>();
        var sp = services.BuildServiceProvider();
        sp.UseBsftSagaFlows();
        return sp;
    }

    [Fact]
    public async Task FullSagaFlow_AdvancesThroughSteps()
    {
        using var sp = BuildProvider();
        var ea = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();

        await ea.PublishAsync(new OrderPlaced("order-1", "customer-42"));

        var instance = await store.FindAsync<OrderSagaState>("OrderSagaState_OrderPlaced", "order-1");
        instance.Should().NotBeNull();
        instance!.State.CustomerId.Should().Be("customer-42");
        instance.CurrentStepIndex.Should().Be(1);

        await ea.PublishAsync(new PaymentReceived("order-1", 99.95m));

        instance = await store.FindAsync<OrderSagaState>("OrderSagaState_OrderPlaced", "order-1");
        instance.Should().NotBeNull();
        instance!.State.AmountPaid.Should().Be(99.95m);
        instance.CurrentStepIndex.Should().Be(2);

        await ea.PublishAsync(new OrderShipped("order-1"));

        // After the last step with auto-complete, the instance should be removed
        instance = await store.FindAsync<OrderSagaState>("OrderSagaState_OrderPlaced", "order-1");
        instance.Should().BeNull();
    }

    [Fact]
    public async Task UnrelatedEvent_DoesNotAdvanceSaga()
    {
        using var sp = BuildProvider();
        var ea = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();

        await ea.PublishAsync(new OrderPlaced("order-1", "customer-42"));

        // Publish a shipped event before payment (wrong step)
        await ea.PublishAsync(new OrderShipped("order-1"));

        // Saga should still be at step 1 waiting for payment
        var instance = await store.FindAsync<OrderSagaState>("OrderSagaState_OrderPlaced", "order-1");
        instance.Should().NotBeNull();
        instance!.CurrentStepIndex.Should().Be(1);
        instance.State.Shipped.Should().BeFalse();
    }
}
