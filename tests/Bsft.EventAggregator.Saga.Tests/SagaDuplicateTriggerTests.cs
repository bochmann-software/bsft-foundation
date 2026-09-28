using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests;

/// <summary>
/// What a repeated trigger event does to a saga that is already running. The live-instance cache
/// answers that for as long as the instance is cached, but the cache is per process and volatile:
/// after a restart, or once an instance has been dropped, only the store still knows the saga
/// exists.
/// </summary>
public class SagaDuplicateTriggerTests
{
    public record OrderPlaced(string OrderId);
    public record PaymentReceived(string OrderId);

    public class OrderState
    {
        public bool Paid { get; set; }

        public int TriggerRuns { get; set; }
    }

    public class OrderFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderPlaced>(e => e.OrderId)
                .Then((ctx, _) =>
                {
                    ctx.State.TriggerRuns++;
                    return Task.CompletedTask;
                })
                .WaitFor<PaymentReceived>(e => e.OrderId)
                .Then((ctx, _) =>
                {
                    ctx.State.Paid = true;
                    return Task.CompletedTask;
                })
                .Build();
        }
    }

    [Fact]
    public async Task ADuplicateTriggerDoesNotResetASagaTheCacheHasForgotten()
    {
        using var sp = BuildProvider();
        var events = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();
        var registry = sp.GetRequiredService<ISagaInstanceRegistry>();

        await events.PublishAsync(new OrderPlaced("o-1"));
        await events.PublishAsync(new PaymentReceived("o-1"));

        var sagaType = (await store.FindAsync<OrderState>("OrderState_OrderPlaced", "o-1"))!.SagaType;

        // Stand in for a restart: the store still holds the saga, the in-process cache does not.
        registry.Remove(registry.KeyFor(sagaType, "o-1"));

        await events.PublishAsync(new OrderPlaced("o-1"));

        var persisted = await store.FindAsync<OrderState>(sagaType, "o-1");
        persisted.Should().NotBeNull();
        persisted!.State.Paid.Should().BeTrue("the saga had already taken the payment step");
        persisted.State.TriggerRuns.Should().Be(1, "the trigger step must not run a second time");
        persisted.CurrentStepIndex.Should().Be(2, "the saga must not be rewound to its first wait");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        services.AddBsftSagaFlow<OrderFlow>();
        var sp = services.BuildServiceProvider();
        sp.UseBsftSagaFlows();
        return sp;
    }
}
