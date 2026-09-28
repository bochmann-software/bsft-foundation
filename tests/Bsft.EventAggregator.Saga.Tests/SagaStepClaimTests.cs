using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests;

/// <summary>
/// Claiming a step. The timeout dispatcher publishes from a background thread, so a business event
/// and a due timer can reach the same waiting step at once; only one of them may advance it.
/// </summary>
/// <remarks>
/// The thread-level data race the striped gate closes cannot be forced deterministically in-process,
/// so these tests pin what is observable instead: a publisher that loses the claim stops rather than
/// running the step body, a second event arriving after the step advanced is dropped, and a step that
/// publishes the next step's event from inside itself does not deadlock. The last one is the reason
/// the gate covers only the compare-and-advance and never the step body, the store read or the
/// condition predicate.
/// </remarks>
public class SagaStepClaimTests
{
    public record OrderPlaced(string OrderId);
    public record PaymentReceived(string OrderId);
    public record PaymentOverdue(string OrderId);
    public record ShipmentReady(string OrderId);

    public class OrderState
    {
        public int SecondStepRuns { get; set; }

        public bool Shipped { get; set; }
    }

    /// <summary>Two competing events for the same waiting step.</summary>
    public class CompetingEventsFlow : ISagaFlow
    {
        public static int Runs;

        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderPlaced>(e => e.OrderId)
                .Then((_, _) => Task.CompletedTask)
                .WaitFor<PaymentReceived>(e => e.OrderId)
                .Then((ctx, _) =>
                {
                    Interlocked.Increment(ref Runs);
                    ctx.State.SecondStepRuns++;
                    return Task.CompletedTask;
                })
                .Or<PaymentOverdue>(e => e.OrderId)
                .Then((ctx, _) =>
                {
                    Interlocked.Increment(ref Runs);
                    ctx.State.SecondStepRuns++;
                    return Task.CompletedTask;
                })
                .Complete();
        }
    }

    /// <summary>A step that publishes the next step's event from inside its own body.</summary>
    public class ReentrantFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderPlaced>(e => e.OrderId)
                .Then((_, _) => Task.CompletedTask)
                .WaitFor<PaymentReceived>(e => e.OrderId)
                .Then(async (ctx, e) => await ctx.PublishAsync(new ShipmentReady(e.OrderId)))
                .WaitFor<ShipmentReady>(e => e.OrderId)
                .Then((ctx, _) =>
                {
                    ctx.State.Shipped = true;
                    return Task.CompletedTask;
                })
                .Complete();
        }
    }

    /// <summary>
    /// The step condition publishes the competing event from inside itself. That is pathological user
    /// code, but it is the one way to land two publishers on the same waiting step deterministically:
    /// the inner publish claims and advances the step while the outer one is still evaluating its
    /// condition, so the outer one must lose the claim and stop.
    /// </summary>
    public class ConditionPublishesCompetitorFlow : ISagaFlow
    {
        public static IEventAggregator? Events;
        public static int Runs;

        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderPlaced>(e => e.OrderId)
                .Then((_, _) => Task.CompletedTask)
                .WaitFor<PaymentReceived>(e => e.OrderId)
                .When(
                    (e, _) =>
                    {
                        Events!.PublishAsync(new PaymentOverdue(e.OrderId)).GetAwaiter().GetResult();
                        return true;
                    },
                    (ctx, _) =>
                    {
                        Interlocked.Increment(ref Runs);
                        ctx.State.SecondStepRuns++;
                        return Task.CompletedTask;
                    })
                .Or<PaymentOverdue>(e => e.OrderId)
                .Then((ctx, _) =>
                {
                    Interlocked.Increment(ref Runs);
                    ctx.State.SecondStepRuns++;
                    return Task.CompletedTask;
                })
                .Complete();
        }
    }

    /// <summary>
    /// A flow that drives the retry of a failed step into a duplicate trigger. The failed attempt
    /// drops the cached instance, so the retry reads the saga from the store; the condition of that
    /// retry then republishes the trigger, which finds the cache empty, starts a second instance and
    /// caches it. The claim is therefore granted on a different object than the retry resolved.
    /// </summary>
    public class RetryMeetingADuplicateTriggerFlow : ISagaFlow
    {
        public static IEventAggregator? Events;

        public static bool FailTheStep;

        public static bool RepublishTheTrigger;

        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderPlaced>(e => e.OrderId)
                .Then((_, _) => Task.CompletedTask)
                .WaitFor<PaymentReceived>(e => e.OrderId)
                .When(
                    (e, _) =>
                    {
                        if (RepublishTheTrigger)
                        {
                            RepublishTheTrigger = false;
                            Events!.PublishAsync(new OrderPlaced(e.OrderId)).GetAwaiter().GetResult();
                        }

                        return true;
                    },
                    (ctx, _) =>
                    {
                        if (FailTheStep)
                        {
                            FailTheStep = false;
                            throw new InvalidOperationException("step failed");
                        }

                        ctx.State.SecondStepRuns++;
                        return Task.CompletedTask;
                    })
                .WaitFor<ShipmentReady>(e => e.OrderId)
                .Then((ctx, _) =>
                {
                    ctx.State.Shipped = true;
                    return Task.CompletedTask;
                })
                .Build();
        }
    }

    [Fact]
    public void AClaimGrantedOnACachedInstance_CarriesThatInstance()
    {
        var registry = new SagaInstanceRegistry();
        var key = registry.KeyFor("OrderState_OrderPlaced", "o-1");

        var cached = new SagaInstance<OrderState>("OrderState_OrderPlaced", "o-1", new OrderState())
        {
            CurrentStepIndex = 1,
        };
        registry.TryStart(key, cached).Should().BeTrue();

        // What a caller holds when it read the saga from the store while the cache was still empty.
        var readFromStore = new SagaInstance<OrderState>("OrderState_OrderPlaced", "o-1", new OrderState())
        {
            CurrentStepIndex = 1,
        };

        var claim = registry.ClaimStep(key, readFromStore, 1);

        claim.Should().NotBeNull();
        claim!.PreviousStepIndex.Should().Be(1);
        claim.Instance.Should().BeSameAs(cached, "the step has to run on the instance that was advanced");
        cached.CurrentStepIndex.Should().Be(2);
        readFromStore.CurrentStepIndex.Should().Be(1, "the caller's object was never advanced");
    }

    [Fact]
    public async Task AClaimGrantedOnANewerInstance_LeavesTheStoreAndTheCacheAgreeing()
    {
        using var sp = BuildProvider<RetryMeetingADuplicateTriggerFlow>();
        var events = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();
        RetryMeetingADuplicateTriggerFlow.Events = events;

        await events.PublishAsync(new OrderPlaced("o-1"));

        // The failed attempt gives the claim back and drops the cached instance, so the retry below
        // has to read the saga from the store.
        RetryMeetingADuplicateTriggerFlow.FailTheStep = true;
        var failing = async () => await events.PublishAsync(new PaymentReceived("o-1"));
        await failing.Should().ThrowAsync<AggregateException>();

        RetryMeetingADuplicateTriggerFlow.RepublishTheTrigger = true;
        await events.PublishAsync(new PaymentReceived("o-1"));

        // Persisting the resolved instance instead of the claimed one would write a saga still
        // sitting at the index it was waiting at, while the cache has already moved past it.
        var persisted = await store.FindAsync<OrderState>("OrderState_OrderPlaced", "o-1");
        persisted!.CurrentStepIndex.Should().Be(2, "the claimed instance is the one that advanced");
    }

    [Fact]
    public async Task APublisherThatLosesTheClaimStopsInsteadOfRunningTheStep()
    {
        ConditionPublishesCompetitorFlow.Runs = 0;
        using var sp = BuildProvider<ConditionPublishesCompetitorFlow>();
        var events = sp.GetRequiredService<IEventAggregator>();
        ConditionPublishesCompetitorFlow.Events = events;

        await events.PublishAsync(new OrderPlaced("o-1"));
        await events.PublishAsync(new PaymentReceived("o-1"));

        ConditionPublishesCompetitorFlow.Runs.Should().Be(1, "the loser of the claim must not run the step body");
    }

    [Fact]
    public async Task OnlyOneOfTwoCompetingEventsAdvancesAWaitingStep()
    {
        CompetingEventsFlow.Runs = 0;
        using var sp = BuildProvider<CompetingEventsFlow>();
        var events = sp.GetRequiredService<IEventAggregator>();

        await events.PublishAsync(new OrderPlaced("o-1"));

        await events.PublishAsync(new PaymentReceived("o-1"));
        await events.PublishAsync(new PaymentOverdue("o-1"));

        CompetingEventsFlow.Runs.Should().Be(1, "the second event arrives at a step that has moved on");
    }

    [Fact]
    public async Task AStepThatPublishesTheNextStepsEventFromInsideItselfDoesNotDeadlock()
    {
        using var sp = BuildProvider<ReentrantFlow>();
        var events = sp.GetRequiredService<IEventAggregator>();
        await events.PublishAsync(new OrderPlaced("o-1"));

        // A gate held across the step body would deadlock here: the inner publish re-enters the same
        // saga instance on the same call stack. It has to complete, and the saga has to finish.
        var publish = events.PublishAsync(new PaymentReceived("o-1"));

        var finished = await Task.WhenAny(publish, Task.Delay(TimeSpan.FromSeconds(10)));
        finished.Should().BeSameAs(publish, "the reentrant publish must not block on the step gate");
        await publish;

        (await sp.GetRequiredService<ISagaStore>().FindAsync<OrderState>("OrderState_OrderPlaced", "o-1"))
            .Should().BeNull("the saga ran through to completion");
    }

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
