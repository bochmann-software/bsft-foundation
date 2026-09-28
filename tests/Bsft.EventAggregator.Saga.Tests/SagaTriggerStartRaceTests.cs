using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests;

/// <summary>
/// What a concurrent event for the following step does while a trigger event is still suspended in
/// the store round trip of the duplicate-start check. The check reads a persistent store, so the
/// suspension is real; nothing may become claimable for the key while it lasts.
/// </summary>
public class SagaTriggerStartRaceTests
{
    public record OrderPlaced(string OrderId);

    public record PaymentReceived(string OrderId);

    public class RaceState
    {
        public bool TriggerRan { get; set; }

        public bool PaymentRan { get; set; }

        public bool PaymentSawTriggerRan { get; set; }
    }

    public class RaceFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            factory.WithState<RaceState>()
                .StartWith<OrderPlaced>(e => e.OrderId)
                .Then((ctx, _) =>
                {
                    ctx.State.TriggerRan = true;
                    return Task.CompletedTask;
                })
                .WaitFor<PaymentReceived>(e => e.OrderId)
                .Then((ctx, _) =>
                {
                    ctx.State.PaymentRan = true;
                    ctx.State.PaymentSawTriggerRan = ctx.State.TriggerRan;
                    return Task.CompletedTask;
                })
                .Build();
        }
    }

    [Fact]
    public async Task AConcurrentStepEventCannotClaimASagaWhoseTriggerStepHasNotRunYet()
    {
        var store = new GatedSagaStore();
        using var sp = BuildProvider(store);
        var events = sp.GetRequiredService<IEventAggregator>();

        // The trigger suspends inside the duplicate-start check, on the store round trip.
        var trigger = events.PublishAsync(new OrderPlaced("o-1"));
        await store.FirstReadReached;

        // A genuinely concurrent event for the following step, for the same correlation key.
        await events.PublishAsync(new PaymentReceived("o-1"));

        store.ReleaseFirstRead();
        await trigger;

        var persisted = await store.ReadDirectAsync<RaceState>("RaceState_OrderPlaced", "o-1");
        persisted.Should().NotBeNull("the trigger step started the saga and persisted it");

        using var _ = new AssertionScope();
        persisted!.State.TriggerRan.Should().BeTrue("the trigger step body ran");
        persisted.State.PaymentRan.Should().BeFalse(
            "nothing may claim a step of a saga whose trigger step has not run yet");
        persisted.CurrentStepIndex.Should().Be(1, "the saga waits at its first step after the trigger");
    }

    private static ServiceProvider BuildProvider(GatedSagaStore store)
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();

        // Registered after the engine so this store, not the default in-memory one, is resolved.
        services.AddSingleton<ISagaStore>(store);

        services.AddBsftSagaFlow<RaceFlow>();
        var sp = services.BuildServiceProvider();
        sp.UseBsftSagaFlows();
        return sp;
    }

    /// <summary>
    /// An in-memory store whose very first read blocks until it is released, which turns the store
    /// round trip of the duplicate-start check into a suspension the test controls.
    /// </summary>
    private sealed class GatedSagaStore : ISagaStore
    {
        private readonly InMemorySagaStore _inner = new();
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;

        /// <summary>Gets a task that completes once the first read has been entered.</summary>
        public Task FirstReadReached => _reached.Task;

        /// <summary>Lets the blocked first read return.</summary>
        public void ReleaseFirstRead() => _release.TrySetResult();

        /// <summary>Reads without passing the gate, for asserting the persisted outcome.</summary>
        public Task<SagaInstance<TState>?> ReadDirectAsync<TState>(string sagaType, string correlationKey) =>
            _inner.FindAsync<TState>(sagaType, correlationKey);

        public async Task<SagaInstance<TState>?> FindAsync<TState>(
            string sagaType, string correlationKey, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _reads) == 1)
            {
                _reached.TrySetResult();
                await _release.Task.ConfigureAwait(false);
            }

            return await _inner.FindAsync<TState>(sagaType, correlationKey, ct).ConfigureAwait(false);
        }

        public Task SaveAsync<TState>(SagaInstance<TState> instance, CancellationToken ct = default) =>
            _inner.SaveAsync(instance, ct);

        public Task DeleteAsync(string sagaType, string correlationKey, CancellationToken ct = default) =>
            _inner.DeleteAsync(sagaType, correlationKey, ct);
    }
}
