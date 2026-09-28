using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Timeouts;

/// <summary>
/// End-to-end behaviour of timeouts inside a running saga: the sales flow from the issue —
/// quote sent, reminder after two days, cancelled when the customer replies first.
/// </summary>
public class SagaTimeoutFlowTests
{
    public record QuoteSent(string QuoteId);
    public record QuoteFollowUpDue(string QuoteId);
    public record QuoteAccepted(string QuoteId);

    public class QuoteState
    {
        public bool Accepted { get; set; }
    }

    public class SalesFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<QuoteState>()
                .StartWith<QuoteSent>(e => e.QuoteId)
                .Then((ctx, e) =>
                {
                    ctx.ScheduleTimeout("followup", TimeSpan.FromDays(2), new QuoteFollowUpDue(e.QuoteId));
                    ctx.ScheduleTimeout("callback", TimeSpan.FromDays(3), new QuoteFollowUpDue(e.QuoteId));
                    return Task.CompletedTask;
                })
                .WaitFor<QuoteAccepted>(e => e.QuoteId)
                .Then((ctx, _) =>
                {
                    ctx.State.Accepted = true;
                    return Task.CompletedTask;
                })
                .Complete();
        }
    }

    public class UncorrelatedFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<QuoteState>()
                .StartWith<QuoteSent>()
                .Then((ctx, e) =>
                {
                    ctx.ScheduleTimeout("followup", TimeSpan.FromDays(2), new QuoteFollowUpDue(e.QuoteId));
                    return Task.CompletedTask;
                })
                .Build();
        }
    }

    /// <summary>No correlation key, schedules a timeout, and completes in the same step.</summary>
    public class UncorrelatedAutoCompletingFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<QuoteState>()
                .StartWith<QuoteSent>()
                .Then((ctx, e) =>
                {
                    ctx.ScheduleTimeout("followup", TimeSpan.FromDays(2), new QuoteFollowUpDue(e.QuoteId));
                    return Task.CompletedTask;
                })
                .Complete();
        }
    }

    public class UncorrelatedCancelingFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<QuoteState>()
                .StartWith<QuoteSent>()
                .Then((ctx, _) =>
                {
                    ctx.CancelAllTimeouts();
                    return Task.CompletedTask;
                })
                .Build();
        }
    }

    public class CompletingWhileSchedulingFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<QuoteState>()
                .StartWith<QuoteSent>(e => e.QuoteId)
                .Then((ctx, e) =>
                {
                    ctx.ScheduleTimeout("followup", TimeSpan.FromDays(2), new QuoteFollowUpDue(e.QuoteId));
                    ctx.MarkComplete();
                    return Task.CompletedTask;
                })
                .Build();
        }
    }

    /// <summary>An auto-completing flow whose last step also schedules a timer.</summary>
    public class AutoCompletingWhileSchedulingFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<QuoteState>()
                .StartWith<QuoteSent>(e => e.QuoteId)
                .Then((ctx, e) =>
                {
                    ctx.ScheduleTimeout("followup", TimeSpan.FromDays(2), new QuoteFollowUpDue(e.QuoteId));
                    return Task.CompletedTask;
                })
                .Complete();
        }
    }

    public class CancelingFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<QuoteState>()
                .StartWith<QuoteSent>(e => e.QuoteId)
                .Then((ctx, e) =>
                {
                    ctx.ScheduleTimeout("followup", TimeSpan.FromDays(2), new QuoteFollowUpDue(e.QuoteId));
                    ctx.CancelTimeout("followup");
                    ctx.CancelAllTimeouts();
                    return Task.CompletedTask;
                })
                .WaitFor<QuoteAccepted>(e => e.QuoteId)
                .Then((_, _) => Task.CompletedTask)
                .Complete();
        }
    }

    [Fact]
    public async Task AStepCanScheduleSeveralTimeoutsAtOnce()
    {
        using var sp = BuildProvider<SalesFlow>();

        await sp.GetRequiredService<IEventAggregator>().PublishAsync(new QuoteSent("q-1"));

        var pending = await PendingAsync(sp);
        pending.Select(t => t.Name).Should().BeEquivalentTo(["followup", "callback"]);
        pending.Should().OnlyContain(t => t.CorrelationKey == "q-1" && t.StepIndex == 1);
    }

    [Fact]
    public async Task TheBusinessEventArrivingFirst_CancelsThePendingReminders()
    {
        using var sp = BuildProvider<SalesFlow>();
        var events = sp.GetRequiredService<IEventAggregator>();
        await events.PublishAsync(new QuoteSent("q-1"));

        await events.PublishAsync(new QuoteAccepted("q-1"));

        (await PendingAsync(sp)).Should().BeEmpty();
    }

    [Fact]
    public async Task ACompletedSaga_LeavesNoTimersBehind()
    {
        using var sp = BuildProvider<SalesFlow>();
        var events = sp.GetRequiredService<IEventAggregator>();

        await events.PublishAsync(new QuoteSent("q-1"));
        await events.PublishAsync(new QuoteAccepted("q-1"));

        (await PendingAsync(sp)).Should().BeEmpty();
        (await sp.GetRequiredService<ISagaStore>().FindAsync<QuoteState>("QuoteState_QuoteSent", "q-1")).Should().BeNull();
    }

    [Fact]
    public async Task AFiredTimeout_IsIgnoredOnceTheSagaHasMovedOn()
    {
        using var sp = BuildProvider<SalesFlow>();
        var events = sp.GetRequiredService<IEventAggregator>();
        await events.PublishAsync(new QuoteSent("q-1"));
        await events.PublishAsync(new QuoteAccepted("q-1"));

        // The already-due timer wins the race and publishes anyway: the step guard drops it.
        var act = async () => await events.PublishAsync(new QuoteFollowUpDue("q-1"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ScheduleAndCancelInTheSameStep_LeaveNothingPending()
    {
        using var sp = BuildProvider<CancelingFlow>();

        await sp.GetRequiredService<IEventAggregator>().PublishAsync(new QuoteSent("q-1"));

        (await PendingAsync(sp)).Should().BeEmpty();
    }

    [Fact]
    public async Task ASagaWithoutACorrelationKey_CannotScheduleATimeout()
    {
        using var sp = BuildProvider<UncorrelatedFlow>();

        var act = async () => await sp.GetRequiredService<IEventAggregator>().PublishAsync(new QuoteSent("q-1"));

        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.Flatten().InnerExceptions.Should()
            .ContainSingle(e => e is InvalidOperationException && e.Message.Contains("correlation key"));
    }

    [Fact]
    public async Task AStepThatCompletesTheSaga_LeavesNoTimerBehindEvenIfItScheduledOne()
    {
        using var sp = BuildProvider<CompletingWhileSchedulingFlow>();

        await sp.GetRequiredService<IEventAggregator>().PublishAsync(new QuoteSent("q-1"));

        (await PendingAsync(sp)).Should().BeEmpty("a completed saga can never react to a reminder");
    }

    [Fact]
    public async Task AnAutoCompletingSagaLeavesNoTimerBehindEither()
    {
        using var sp = BuildProvider<AutoCompletingWhileSchedulingFlow>();

        await sp.GetRequiredService<IEventAggregator>().PublishAsync(new QuoteSent("q-1"));

        // The single step is also the last one, so the saga finishes without MarkComplete() ever
        // being called — the timer must go with it all the same.
        (await PendingAsync(sp)).Should().BeEmpty();
    }

    [Fact]
    public async Task ASagaWithoutACorrelationKey_StillFailsLoudly_EvenWhenTheStepCompletesIt()
    {
        using var sp = BuildProvider<UncorrelatedAutoCompletingFlow>();

        var act = async () => await sp.GetRequiredService<IEventAggregator>().PublishAsync(new QuoteSent("q-1"));

        // Completing the saga must not turn an impossible timeout into a silent no-op.
        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.Flatten().InnerExceptions.Should()
            .ContainSingle(e => e is InvalidOperationException && e.Message.Contains("correlation key"));
    }

    [Fact]
    public async Task ASagaWithoutACorrelationKey_MayStillCancel_WhichIsANoOp()
    {
        using var sp = BuildProvider<UncorrelatedCancelingFlow>();

        var act = async () => await sp.GetRequiredService<IEventAggregator>().PublishAsync(new QuoteSent("q-1"));

        await act.Should().NotThrowAsync();
        (await PendingAsync(sp)).Should().BeEmpty();
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
