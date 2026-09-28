using Bsft.EventAggregator.Saga;

namespace Bsft.Testing.Tests.Support;

/// <summary>
/// Waits two days for a payment, nudges once when the reminder fires, and completes on payment.
/// </summary>
public class OrderFlow : ISagaFlow
{
    public void Configure(ISagaBuilderFactory factory)
    {
        factory.WithState<OrderState>()
            .StartWith<OrderPlaced>(e => e.OrderId)
            .Then((ctx, e) =>
            {
                ctx.ScheduleTimeout("reminder", TimeSpan.FromDays(2), new PaymentReminderDue(e.OrderId));
                return Task.CompletedTask;
            })
            .WaitFor<PaymentReminderDue>(e => e.OrderId)
            .Then((ctx, _) =>
            {
                ctx.State.Reminders++;
                ctx.GetService<ITrace>().Add("reminder-fired");
                return Task.CompletedTask;
            })
            .WaitFor<OrderPaid>(e => e.OrderId)
            .Then((ctx, _) =>
            {
                ctx.State.IsPaid = true;
                return Task.CompletedTask;
            })
            .Complete();
    }
}
