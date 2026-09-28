using Bsft.EventAggregator.Saga;

namespace Bsft.Testing.Tests.Support;

/// <summary>A flow whose timeout step always fails, so a drain can make no progress.</summary>
public class FailingTimeoutFlow : ISagaFlow
{
    public void Configure(ISagaBuilderFactory factory)
    {
        factory.WithState<ShipmentState>()
            .StartWith<ShipmentDispatched>(e => e.ShipmentId)
            .Then((ctx, e) =>
            {
                ctx.ScheduleTimeout("overdue", TimeSpan.FromDays(1), new ShipmentOverdue(e.ShipmentId));
                return Task.CompletedTask;
            })
            .WaitFor<ShipmentOverdue>(e => e.ShipmentId)
            .Then((_, _) => throw new InvalidOperationException("the overdue step is broken"))
            .Build();
    }
}
