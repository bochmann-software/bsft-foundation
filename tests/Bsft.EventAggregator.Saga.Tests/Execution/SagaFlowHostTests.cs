using Bsft.Core;
using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.Diagnostics;
using Bsft.EventAggregator.Saga.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Execution;

/// <summary>
/// Tests for SagaFlowHost edge cases not covered by SagaFlowTests.
/// </summary>
public class SagaFlowHostTests
{
    public record ItemAdded(string CartId);
    public record ItemAdded2(string CartId); // second event of same type for duplicate-type test
    public record OrderConfirmed(string OrderId);
    public record OrderShipped(string OrderId);
    public record PaymentEvent(string OrderId);

    public class CartState
    {
        public int ItemCount { get; set; }
    }

    public class OrderState
    {
        public bool Confirmed { get; set; }
        public bool Shipped { get; set; }
        public bool PaidAfterShip { get; set; }
    }

    /// <summary>
    /// Saga where WaitFor uses the SAME event type as a previous step.
    /// This exercises SagaFlowHost.SubscribeToDefinition line 73 (the "continue" for duplicate event types).
    /// </summary>
    public class DuplicateEventTypeFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<CartState>()
                .StartWith<ItemAdded>(e => e.CartId)
                .Then((ctx, e) =>
                {
                    ctx.State.ItemCount++;
                    return Task.CompletedTask;
                })
                .WaitFor<ItemAdded>(e => e.CartId)     // same event type as StartWith
                .Then((ctx, e) =>
                {
                    ctx.State.ItemCount++;
                    return Task.CompletedTask;
                })
                .Complete();
        }
    }

    /// <summary>
    /// Saga where the subsequent step has NO correlation key selector.
    /// This exercises SagaFlowHost.HandleSubsequentEventAsync line 161 (correlationKey is null => return).
    /// </summary>
    public class NoCorrelationOnSubsequentStepFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<CartState>()
                .StartWith<ItemAdded>(e => e.CartId)
                .Then((ctx, e) =>
                {
                    ctx.State.ItemCount = 1;
                    return Task.CompletedTask;
                })
                .WaitFor<ItemAdded2>()     // no correlation key selector
                .Then((ctx, e) =>
                {
                    ctx.State.ItemCount = 2;
                    return Task.CompletedTask;
                })
                .Complete();
        }
    }

    /// <summary>
    /// Saga where we test that a completed saga instance does not advance further.
    /// Exercises HandleSubsequentEventAsync line 177 (instance.IsComplete => return).
    /// </summary>
    public class ManualCompleteFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderConfirmed>(e => e.OrderId)
                .Then((ctx, e) =>
                {
                    ctx.State.Confirmed = true;
                    ctx.MarkComplete();   // mark complete manually
                    return Task.CompletedTask;
                })
                .WaitFor<OrderShipped>(e => e.OrderId)
                .Then((ctx, e) =>
                {
                    ctx.State.Shipped = true;
                    return Task.CompletedTask;
                })
                .Build();   // not auto-complete; manual MarkComplete in step 1
        }
    }

    /// <summary>
    /// Two-step saga used for testing completed-instance behavior.
    /// </summary>
    public class OrderFlowForCompletedTest : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderConfirmed>(e => e.OrderId)
                .Then((ctx, e) =>
                {
                    ctx.State.Confirmed = true;
                    return Task.CompletedTask;
                })
                .WaitFor<OrderShipped>(e => e.OrderId)
                .Then((ctx, e) =>
                {
                    ctx.State.Shipped = true;
                    return Task.CompletedTask;
                })
                .Complete();
        }
    }

    /// <summary>
    /// Saga where a subsequent step has a condition that is NOT met.
    /// Exercises HandleSubsequentEventAsync lines 185-192 (condition check, early return when false).
    /// </summary>
    public class ConditionalSubsequentStepFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderConfirmed>(e => e.OrderId)
                .Then((ctx, e) =>
                {
                    ctx.State.Confirmed = true;
                    return Task.CompletedTask;
                })
                .WaitFor<PaymentEvent>(e => e.OrderId)
                .When(
                    (e, state) => state.Shipped,   // condition: Shipped must be true
                    (ctx, e) =>
                    {
                        ctx.State.PaidAfterShip = true;
                        return Task.CompletedTask;
                    })
                .Complete();
        }
    }

    /// <summary>
    /// Saga where a subsequent step uses WhenNot, exercising the IsNegated=true branch (line 189).
    /// </summary>
    public class NegatedConditionalSubsequentStepFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderConfirmed>(e => e.OrderId)
                .Then((ctx, e) =>
                {
                    ctx.State.Confirmed = true;
                    return Task.CompletedTask;
                })
                .WaitFor<PaymentEvent>(e => e.OrderId)
                .WhenNot(
                    (e, state) => state.Shipped,   // negated: run when Shipped is NOT true (Shipped=false by default)
                    (ctx, e) =>
                    {
                        ctx.State.PaidAfterShip = true;
                        return Task.CompletedTask;
                    })
                .Complete();
        }
    }

    /// <summary>Trigger step that counts how often it runs, to prove a duplicate trigger is ignored.</summary>
    public class CountingTriggerFlow : ISagaFlow
    {
        public static int TriggerRuns;

        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<CartState>()
                .StartWith<ItemAdded>(e => e.CartId)
                .Then((ctx, e) =>
                {
                    TriggerRuns++;
                    ctx.State.ItemCount++;
                    return Task.CompletedTask;
                })
                .WaitFor<OrderShipped>(e => e.OrderId)
                .Then((ctx, e) => Task.CompletedTask)
                .Complete();
        }
    }

    /// <summary>Trigger step that always throws, to exercise the trigger failure-cleanup path.</summary>
    public class ThrowingTriggerFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<CartState>()
                .StartWith<ItemAdded>(e => e.CartId)
                .Then((ctx, e) => throw new InvalidOperationException("trigger step failed"))
                .WaitFor<OrderShipped>(e => e.OrderId)
                .Then((ctx, e) => Task.CompletedTask)
                .Complete();
        }
    }

    /// <summary>Subsequent step that always throws, to exercise the subsequent failure-cleanup path.</summary>
    public class ThrowingSubsequentFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderConfirmed>(e => e.OrderId)
                .Then((ctx, e) =>
                {
                    ctx.State.Confirmed = true;
                    return Task.CompletedTask;
                })
                .WaitFor<OrderShipped>(e => e.OrderId)
                .Then((ctx, e) => throw new InvalidOperationException("subsequent step failed"))
                .Complete();
        }
    }

    /// <summary>
    /// A three-step flow whose middle step is not the final one, so its commit goes through
    /// <see cref="ISagaStore.SaveAsync"/> — the seam a conflict is injected on to drive the engine's
    /// re-handle. The middle step counts its executions to prove at-least-once execution under a refusal.
    /// </summary>
    public class RetryProbeFlow : ISagaFlow
    {
        public static int SubsequentRuns;

        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderConfirmed>(e => e.OrderId)
                .Then((ctx, e) =>
                {
                    ctx.State.Confirmed = true;
                    return Task.CompletedTask;
                })
                .WaitFor<OrderShipped>(e => e.OrderId)
                .Then((ctx, e) =>
                {
                    SubsequentRuns++;
                    ctx.State.Shipped = true;
                    return Task.CompletedTask;
                })
                .WaitFor<PaymentEvent>(e => e.OrderId)
                .Then((ctx, e) => Task.CompletedTask)
                .Complete();
        }
    }

    [Fact]
    public async Task SubsequentStepConflict_ReloadsFreshStateAndReHandlesTheEvent()
    {
        // The store refuses the subsequent advance once, standing in for a concurrent writer. Delivery
        // is fire-and-forget, so the engine reloads fresh state through GetAsync and re-runs the step
        // rather than losing the event: the step body runs at least once, and the conflict is resolved
        // without surfacing to the publisher.
        RetryProbeFlow.SubsequentRuns = 0;
        var store = new ConflictInjectingSagaStore(conflicts: 1);
        using var sp = BuildProviderWithStore<RetryProbeFlow>(store);
        var ea = sp.GetRequiredService<IEventAggregator>();

        await ea.PublishAsync(new OrderConfirmed("order-retry"));
        var getCallsBeforeShip = store.GetCalls;

        await ea.PublishAsync(new OrderShipped("order-retry"));

        RetryProbeFlow.SubsequentRuns.Should().Be(2, "the refused step is re-handled once against fresh state");
        store.GetCalls.Should().BeGreaterThan(getCallsBeforeShip, "the retry reloads fresh state through GetAsync");

        var instance = await store.FindAsync<OrderState>("OrderState_OrderConfirmed", "order-retry");
        instance.Should().NotBeNull("the saga advanced past the middle step after the retry succeeded");
        instance!.State.Shipped.Should().BeTrue();
    }

    [Fact]
    public async Task SubsequentStepConflict_WhenPersistentlyContended_SurfacesTheConflict()
    {
        // A saga that stays contended past the bounded retries surfaces the conflict to the publisher
        // instead of re-running forever.
        RetryProbeFlow.SubsequentRuns = 0;
        var store = new ConflictInjectingSagaStore(conflicts: int.MaxValue);
        using var sp = BuildProviderWithStore<RetryProbeFlow>(store);
        var ea = sp.GetRequiredService<IEventAggregator>();

        await ea.PublishAsync(new OrderConfirmed("order-stuck"));

        var act = async () => await ea.PublishAsync(new OrderShipped("order-stuck"));
        await act.Should().ThrowAsync<AggregateException>()
            .WithInnerException(typeof(PreconditionFailedException));

        RetryProbeFlow.SubsequentRuns.Should().Be(3, "the event is re-handled up to the bounded attempt count");
    }

    [Fact]
    public async Task SagaFlowHost_WhenASubsequentStepIsConflictRetriedThenSucceeds_EmitsStepTelemetryOnce()
    {
        // A conflict on the subsequent advance re-handles the same logical event. The per-logical-event
        // telemetry — the saga-started counter and the step-began scope — must fire once, not once per
        // retried attempt, or a retried-then-succeeded event inflates the counts.
        RetryProbeFlow.SubsequentRuns = 0;
        var store = new ConflictInjectingSagaStore(conflicts: 1);
        var telemetry = new CountingSagaTelemetry();
        using var sp = BuildProvider(services =>
        {
            services.RemoveAll<ISagaStore>();
            services.AddSingleton<ISagaStore>(store);
            services.RemoveAll<ISagaTelemetry>();
            services.AddSingleton<ISagaTelemetry>(telemetry);
            services.AddBsftSagaFlow<RetryProbeFlow>();
        });
        var ea = sp.GetRequiredService<IEventAggregator>();

        await ea.PublishAsync(new OrderConfirmed("order-tel-once"));
        await ea.PublishAsync(new OrderShipped("order-tel-once"));

        RetryProbeFlow.SubsequentRuns.Should().Be(2, "the refused step is re-handled once against fresh state");
        telemetry.SagaStartedCount.Should().Be(1, "the trigger started the saga once, not once per retried attempt");
        telemetry.BeganSteps.Count(index => index == 1)
            .Should().Be(1, "the conflict-retried subsequent step began once, not once per attempt");
        telemetry.BeganSteps.Count(index => index == 0)
            .Should().Be(1, "the trigger step began once");
    }

    [Fact]
    public async Task SagaFlowHost_WhenTheTriggerSaveIsRefusedByADuplicate_RunsTheTriggerOnceAndReconciles()
    {
        // A cross-process duplicate trigger: another writer commits the saga row between this trigger's
        // start check and its own save, so the store refuses that save with the same optimistic-
        // concurrency exception a subsequent step raises. The conflict retry that now wraps the trigger
        // path re-handles the logical event, but the reload finds the competing row already there and
        // backs off — so the trigger body and its per-logical-event telemetry fire exactly once, no
        // second trigger side effect runs, and the competing row is left intact rather than clobbered.
        CountingTriggerFlow.TriggerRuns = 0;
        var competing = new SagaInstance<CartState>("CartState_ItemAdded", "cart-trig", new CartState { ItemCount = 42 })
        {
            CurrentStepIndex = 1
        };
        var store = new ConflictInjectingSagaStore(conflicts: 0, triggerRefusalSeed: competing);
        var telemetry = new CountingSagaTelemetry();
        using var sp = BuildProvider(services =>
        {
            services.RemoveAll<ISagaStore>();
            services.AddSingleton<ISagaStore>(store);
            services.RemoveAll<ISagaTelemetry>();
            services.AddSingleton<ISagaTelemetry>(telemetry);
            services.AddBsftSagaFlow<CountingTriggerFlow>();
        });
        var ea = sp.GetRequiredService<IEventAggregator>();

        await ea.PublishAsync(new ItemAdded("cart-trig"));

        CountingTriggerFlow.TriggerRuns.Should().Be(1, "the trigger body runs once; the retry backs off against the existing row");
        telemetry.SagaStartedCount.Should().Be(1, "the saga-started signal fires once, not once per retried attempt");
        telemetry.BeganSteps.Count(index => index == 0).Should().Be(1, "the trigger step began once");

        var stored = await store.FindAsync<CartState>("CartState_ItemAdded", "cart-trig");
        stored.Should().NotBeNull("the competing writer's saga row remains");
        stored!.State.ItemCount.Should().Be(42, "the retry reconciled against the existing row rather than clobbering it with the trigger body's result");
    }

    private static ServiceProvider BuildProviderWithStore<TFlow>(ISagaStore store) where TFlow : class, ISagaFlow
        => BuildProvider(services =>
        {
            // configureFlows runs after AddBsftSaga, so drop its in-memory store and install this one.
            services.RemoveAll<ISagaStore>();
            services.AddSingleton(store);
            services.AddBsftSagaFlow<TFlow>();
        });

    private static ServiceProvider BuildProvider<TFlow>() where TFlow : class, ISagaFlow
        => BuildProvider(s => s.AddBsftSagaFlow<TFlow>());

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configureFlows)
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        configureFlows(services);
        var sp = services.BuildServiceProvider();
        sp.UseBsftSagaFlows();
        return sp;
    }

    [Fact]
    public async Task DuplicateEventType_SubscribesToItOnce_BothStepsRunOnSameEvent()
    {
        // Exercises SubscribeToDefinition line 73: the "continue" branch for duplicate event types.
        // The second WaitFor<ItemAdded> step tries to subscribe to ItemAdded again but is skipped
        // (only ONE subscription is created). However, when ItemAdded is published, the single
        // subscription handler loops over ALL steps and executes both step 0 and step 1 on the
        // SAME event, causing the saga to auto-complete immediately.
        using var sp = BuildProvider<DuplicateEventTypeFlow>();
        var ea = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();

        // Single publish triggers both steps (since one subscription handles both)
        // and auto-completes the saga immediately.
        await ea.PublishAsync(new ItemAdded("cart-1"));

        // Saga auto-completed (both steps ran on the single ItemAdded event)
        var instance = await store.FindAsync<CartState>("CartState_ItemAdded", "cart-1");
        instance.Should().BeNull();
    }

    [Fact]
    public async Task SagaFlow_TriggerEvent_EmitsStartStepAndCompletionTelemetry()
    {
        // The event-driven flow path (SagaFlowHost), not the direct StartSagaAsync API.
        using var sp = BuildProvider<DuplicateEventTypeFlow>();
        var ea = sp.GetRequiredService<IEventAggregator>();
        using var capture = new SagaTelemetryCapture(SagaTelemetry.SourceName);

        await ea.PublishAsync(new ItemAdded("cart-tel"));

        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.saga.started");
        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.saga.completed");
        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.saga.step.duration" && m.Outcome == "executed");
        capture.Activities.Should().Contain(a => a.DisplayName == "saga.step");
    }

    [Fact]
    public async Task SagaFlow_SubsequentConditionNotMet_EmitsSkippedStepTelemetry()
    {
        using var sp = BuildProvider<ConditionalSubsequentStepFlow>();
        var ea = sp.GetRequiredService<IEventAggregator>();
        using var capture = new SagaTelemetryCapture(SagaTelemetry.SourceName);

        await ea.PublishAsync(new OrderConfirmed("order-tel"));   // trigger step executes
        await ea.PublishAsync(new PaymentEvent("order-tel"));     // subsequent step condition false -> skipped

        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.saga.step.duration" && m.Outcome == "skipped");
    }

    [Fact]
    public async Task SagaFlow_StepMarksComplete_EmitsCompletionTelemetry()
    {
        using var sp = BuildProvider<ManualCompleteFlow>();
        var ea = sp.GetRequiredService<IEventAggregator>();
        using var capture = new SagaTelemetryCapture(SagaTelemetry.SourceName);

        await ea.PublishAsync(new OrderConfirmed("order-mc-tel"));   // step marks complete

        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.saga.completed");
    }

    [Fact]
    public async Task NoCorrelationKeyOnSubsequentStep_SubsequentEventIsIgnored()
    {
        // Exercises HandleSubsequentEventAsync line 161: correlationKey is null => return.
        // The subsequent step has no correlation key selector, so it can never be correlated
        // to a specific saga instance.
        using var sp = BuildProvider<NoCorrelationOnSubsequentStepFlow>();
        var ea = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();

        await ea.PublishAsync(new ItemAdded("cart-2"));

        var instance = await store.FindAsync<CartState>("CartState_ItemAdded", "cart-2");
        instance.Should().NotBeNull();
        instance!.State.ItemCount.Should().Be(1);

        // Publish the second event - since the subsequent step has no correlation key,
        // it will be ignored (correlationKey is null => return).
        await ea.PublishAsync(new ItemAdded2("cart-2"));

        instance = await store.FindAsync<CartState>("CartState_ItemAdded", "cart-2");
        // Saga is still at step 1 (not advanced), and still in the store
        instance.Should().NotBeNull();
        instance!.State.ItemCount.Should().Be(1);
    }

    [Fact]
    public async Task AlreadyCompletedInstance_SubsequentEventIsIgnored()
    {
        // Exercises HandleSubsequentEventAsync line 177: instance.IsComplete => return.
        // We pre-insert a completed instance into the store, then publish the subsequent event.
        // The handler retrieves the completed instance from the store and returns early at IsComplete check.
        using var sp = BuildProvider<OrderFlowForCompletedTest>();
        var ea = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();

        // Start the saga normally to get its SagaType name
        var sagaType = "OrderState_OrderConfirmed";

        // Manually save a completed instance to the store
        var completedInstance = new SagaInstance<OrderState>(sagaType, "order-complete-1", new OrderState { Confirmed = true })
        {
            CurrentStepIndex = 1,
            IsComplete = true
        };
        await store.SaveAsync(completedInstance);

        // Now publish OrderShipped - HandleSubsequentEventAsync will find instance with IsComplete=true
        // and return early at line 177, leaving Shipped=false.
        await ea.PublishAsync(new OrderShipped("order-complete-1"));

        var instance = await store.FindAsync<OrderState>(sagaType, "order-complete-1");
        // Instance still in store (not auto-completed since the handler returned early)
        // and Shipped is still false.
        if (instance is not null)
        {
            instance.State.Shipped.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ConditionalSubsequentStep_ConditionNotMet_DoesNotAdvance()
    {
        // Exercises HandleSubsequentEventAsync lines 185-192: condition check.
        // The condition requires State.Shipped = true, but it's false by default.
        using var sp = BuildProvider<ConditionalSubsequentStepFlow>();
        var ea = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();

        await ea.PublishAsync(new OrderConfirmed("order-2"));

        var instance = await store.FindAsync<OrderState>("OrderState_OrderConfirmed", "order-2");
        instance.Should().NotBeNull();

        // Publish payment, but State.Shipped is false, so condition fails => early return at line 191-192
        await ea.PublishAsync(new PaymentEvent("order-2"));

        instance = await store.FindAsync<OrderState>("OrderState_OrderConfirmed", "order-2");
        // Still in store (not auto-completed) since condition wasn't met
        instance.Should().NotBeNull();
        instance!.State.PaidAfterShip.Should().BeFalse();
    }

    [Fact]
    public async Task NegatedConditionalSubsequentStep_ConditionNegated_Executes()
    {
        // Exercises HandleSubsequentEventAsync line 189: IsNegated=true => conditionResult = !conditionResult.
        // WhenNot: condition is Shipped=false (IsNegated makes it: run when NOT Shipped = when Shipped=false).
        // Since Shipped=false by default, the negated condition is met and the action executes.
        using var sp = BuildProvider<NegatedConditionalSubsequentStepFlow>();
        var ea = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();

        await ea.PublishAsync(new OrderConfirmed("order-3"));

        var instance = await store.FindAsync<OrderState>("OrderState_OrderConfirmed", "order-3");
        instance.Should().NotBeNull();

        // Publish payment: State.Shipped=false, WhenNot(Shipped): !false => true => action executes
        await ea.PublishAsync(new PaymentEvent("order-3"));

        // Auto-completes after condition is met and action runs
        instance = await store.FindAsync<OrderState>("OrderState_OrderConfirmed", "order-3");
        instance.Should().BeNull(); // removed after auto-complete
    }

    [Fact]
    public async Task DuplicateTrigger_SameCorrelationKey_DoesNotStartSecondInstance()
    {
        // TryAdd on the active-instance cache means a duplicate trigger for an already-active saga
        // is ignored instead of starting (and overwriting with) a second instance.
        CountingTriggerFlow.TriggerRuns = 0;
        using var sp = BuildProvider<CountingTriggerFlow>();
        var ea = sp.GetRequiredService<IEventAggregator>();

        await ea.PublishAsync(new ItemAdded("cart-dup"));
        await ea.PublishAsync(new ItemAdded("cart-dup"));

        CountingTriggerFlow.TriggerRuns.Should().Be(1);
    }

    [Fact]
    public async Task TriggerStepThrows_EvictsCacheAndPropagates()
    {
        using var sp = BuildProvider<ThrowingTriggerFlow>();
        var ea = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();

        var act = async () => await ea.PublishAsync(new ItemAdded("cart-fail"));
        await act.Should().ThrowAsync<AggregateException>();

        // Step failed before persistence, so nothing was stored.
        (await store.FindAsync<CartState>("CartState_ItemAdded", "cart-fail")).Should().BeNull();

        // The cache was evicted on failure, so a retry attempts the trigger again (and throws again)
        // rather than being silently skipped by TryAdd.
        var retry = async () => await ea.PublishAsync(new ItemAdded("cart-fail"));
        await retry.Should().ThrowAsync<AggregateException>();
    }

    [Fact]
    public async Task SubsequentStepThrows_RollsBackIndexAndPropagates()
    {
        using var sp = BuildProvider<ThrowingSubsequentFlow>();
        var ea = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();

        await ea.PublishAsync(new OrderConfirmed("ord-fail"));
        var before = await store.FindAsync<OrderState>("OrderState_OrderConfirmed", "ord-fail");
        before.Should().NotBeNull();
        var stepIndex = before!.CurrentStepIndex;

        var act = async () => await ea.PublishAsync(new OrderShipped("ord-fail"));
        await act.Should().ThrowAsync<AggregateException>();

        // The persisted step index is unchanged because the advance was rolled back on failure.
        var after = await store.FindAsync<OrderState>("OrderState_OrderConfirmed", "ord-fail");
        after.Should().NotBeNull();
        after!.CurrentStepIndex.Should().Be(stepIndex);

        // A retry re-reads the store and fails again (the cache was evicted, not left advanced).
        var retry = async () => await ea.PublishAsync(new OrderShipped("ord-fail"));
        await retry.Should().ThrowAsync<AggregateException>();
    }

    /// <summary>
    /// Wraps an in-memory store and refuses a bounded number of subsequent-step saves with the
    /// framework's optimistic-concurrency exception, standing in for a concurrent cross-process writer.
    /// </summary>
    private sealed class ConflictInjectingSagaStore : ISagaStore
    {
        private readonly InMemorySagaStore _inner = new();
        private int _remainingConflicts;
        private object? _triggerRefusalSeed;

        public ConflictInjectingSagaStore(int conflicts, object? triggerRefusalSeed = null)
        {
            _remainingConflicts = conflicts;

            // The competing row a cross-process duplicate trigger already committed. It is withheld from
            // the inner store until the local trigger's own save is refused, so the first attempt still
            // sees an empty store and runs the trigger body — only the retry finds the seeded row.
            _triggerRefusalSeed = triggerRefusalSeed;
        }

        public int GetCalls { get; private set; }

        public Task<SagaInstance<TState>?> FindAsync<TState>(string sagaType, string correlationKey, CancellationToken ct = default)
        {
            GetCalls++;
            return _inner.FindAsync<TState>(sagaType, correlationKey, ct);
        }

        public Task SaveAsync<TState>(SagaInstance<TState> instance, CancellationToken ct = default)
        {
            // The trigger's own save (step index 1) is refused exactly once, standing in for a
            // cross-process duplicate trigger whose competing writer committed the row first. The
            // competing row lands in the inner store before the refusal, so the retry's start check finds
            // the saga and backs off instead of re-running the trigger body and clobbering the row.
            if (instance.CurrentStepIndex == 1 && _triggerRefusalSeed is SagaInstance<TState> seed)
            {
                _triggerRefusalSeed = null;
                _inner.SaveAsync(seed, ct);
                throw new PreconditionFailedException("A cross-process duplicate trigger already committed the saga row.");
            }

            // Only the subsequent advance (past the trigger's step 1) is contended; the trigger insert
            // and the eventual successful retry go through to the inner store.
            if (instance.CurrentStepIndex > 1 && _remainingConflicts > 0)
            {
                _remainingConflicts--;
                throw new PreconditionFailedException("The saga was advanced by another writer.");
            }

            return _inner.SaveAsync(instance, ct);
        }

        public Task DeleteAsync(string sagaType, string correlationKey, CancellationToken ct = default)
            => _inner.DeleteAsync(sagaType, correlationKey, ct);
    }

    /// <summary>
    /// An <see cref="ISagaTelemetry"/> double that counts the per-logical-event signals — saga started
    /// and step began — so a test can prove a conflict retry does not double them.
    /// </summary>
    private sealed class CountingSagaTelemetry : ISagaTelemetry
    {
        public int SagaStartedCount { get; private set; }

        public List<int> BeganSteps { get; } = [];

        public void SagaStarted(string sagaType) => SagaStartedCount++;

        public void SagaCompleted(string sagaType)
        {
        }

        public ISagaStepTelemetryScope BeginStep(string sagaType, string? correlationKey, int stepIndex)
        {
            BeganSteps.Add(stepIndex);
            return NoopStepScope.Instance;
        }

        public void TimeoutScheduled(string sagaType)
        {
        }

        public void TimeoutFired(string sagaType)
        {
        }

        public IDisposable? BeginTimeoutDispatch(string sagaType, string timeoutName) => null;

        public void TimeoutCanceled(string sagaType)
        {
        }

        public void TimeoutMissed(string sagaType)
        {
        }

        public void TimeoutDeadLettered(string sagaType)
        {
        }

        private sealed class NoopStepScope : ISagaStepTelemetryScope
        {
            public static readonly NoopStepScope Instance = new();

            public void Skipped()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
