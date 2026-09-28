using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Timeouts;

/// <summary>
/// Which step index a newly scheduled reminder is stamped with. It has to be the index the saga
/// waits at after the recording step, because that is the index whose timers are dropped when the
/// saga moves on. The interesting case is a step that publishes the next step's event synchronously:
/// the in-process aggregator advances the shared instance before the timers are written.
/// </summary>
public class SagaTimeoutStepIndexTests
{
    public record OrderPlaced(string OrderId);
    public record PaymentReceived(string OrderId);
    public record ShipmentConfirmed(string OrderId);
    public record DeliveryOverdue(string OrderId);

    public class OrderState
    {
        public bool Shipped { get; set; }
    }

    /// <summary>
    /// The payment step schedules a reminder for the wait it is entering and, in the same body,
    /// publishes the event of the step after it. The reminder still belongs to the shipment wait.
    /// </summary>
    public class ReentrantPublishFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderPlaced>(e => e.OrderId)
                .Then((_, _) => Task.CompletedTask)
                .WaitFor<PaymentReceived>(e => e.OrderId)
                .Then(async (ctx, e) =>
                {
                    ctx.ScheduleTimeout("delivery", TimeSpan.FromDays(2), new DeliveryOverdue(e.OrderId));
                    await ctx.PublishAsync(new ShipmentConfirmed(e.OrderId));
                })
                .WaitFor<ShipmentConfirmed>(e => e.OrderId)
                .Then((ctx, _) =>
                {
                    ctx.State.Shipped = true;
                    return Task.CompletedTask;
                })
                .Build();
        }
    }

    [Fact]
    public async Task AReminderIsStampedWithTheWaitItGuards_NotWithWhereAReentrantPublishLeftTheSaga()
    {
        using var sp = BuildProvider<ReentrantPublishFlow>();
        var events = sp.GetRequiredService<IEventAggregator>();

        await events.PublishAsync(new OrderPlaced("o-1"));
        await events.PublishAsync(new PaymentReceived("o-1"));

        // The shipment step ran reentrantly and left the shared instance at index 3, but the reminder
        // guards the shipment wait at index 2. Reading the index back off the instance stamped it 3,
        // which ties the timer to a wait it has nothing to do with — which of the two cancellations
        // then happens to reach it is an accident of how far the reentrant chain ran.
        var pending = await PendingAsync(sp);
        pending.Should().ContainSingle().Which.StepIndex.Should().Be(2);
    }

    private static async Task<IReadOnlyList<SagaTimeout>> PendingAsync(IServiceProvider sp) =>
        await sp.GetRequiredService<ISagaTimeoutStore>().ListDueAsync(DateTimeOffset.MaxValue, 100);

    private static ServiceProvider BuildProvider<TFlow>() where TFlow : class, ISagaFlow
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        services.AddBsftSagaFlow<TFlow>();
        var sp = services.BuildServiceProvider();
        sp.UseBsftSagaFlows();
        return sp;
    }
}
