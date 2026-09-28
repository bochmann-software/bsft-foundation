using FluentAssertions;
using Xunit;

namespace Bsft.EventAggregator.Tests;

/// <summary>
/// Regression coverage for the subscribe/unsubscribe race that the base-class extraction opened: because
/// the Action-overload bookkeeping and the forward call into the derived handler storage were guarded by
/// two separate locks, a concurrent <see cref="EventAggregatorBase.Unsubscribe{TEvent}(Action{TEvent})"/>
/// could slip between recording the wrapper and adding it to the handler list and leave the handler
/// permanently subscribed. The fix shares one <see cref="EventAggregatorBase.SyncRoot"/> lock across both
/// steps, making them atomic.
/// </summary>
public class EventAggregatorSubscribeRaceTests
{
    public record TestEvent(string Message);

    [Fact]
    public async Task Subscribe_WhileUnsubscribeRacesTheInFlightForwardCall_DoesNotLeaveHandlerOrphaned()
    {
        // The gated aggregator blocks inside its Func-based Subscribe on the first call. Because the base
        // holds SyncRoot across the forward call, a concurrent Unsubscribe(Action) is forced to wait for
        // the subscribe to finish before it can remove the wrapper. This deterministically drives the
        // exact interleaving that used to orphan the wrapper (subscribe recorded, unsubscribe runs, then
        // subscribe adds to the list) and proves it can no longer happen.
        var aggregator = new GatedAggregator();
        var callCount = 0;
        Action<TestEvent> handler = _ => Interlocked.Increment(ref callCount);

        var subscribeTask = Task.Run(() => aggregator.Subscribe(handler));

        // Wait until the forward Subscribe(Func) call is parked inside the gate while still holding
        // SyncRoot, so the racing Unsubscribe genuinely overlaps the in-flight subscribe.
        aggregator.ForwardCallEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        var unsubscribeTask = Task.Run(() => aggregator.Unsubscribe(handler));

        // Give the Unsubscribe task a moment to reach (and block on) SyncRoot before releasing the gate,
        // so the removal is guaranteed to observe the fully added wrapper rather than an empty list.
        await Task.Delay(50);
        aggregator.ReleaseForwardCall();

        await Task.WhenAll(subscribeTask, unsubscribeTask);

        await aggregator.PublishAsync(new TestEvent("after-race"));

        callCount.Should().Be(0, "the wrapper must not survive an Unsubscribe that races the in-flight Subscribe");
    }

    /// <summary>
    /// Aggregator whose Func-based <see cref="Subscribe{TEvent}(Func{TEvent, Task})"/> blocks on a gate the
    /// first time it is invoked, giving the test a controllable seam to force the subscribe/unsubscribe
    /// interleaving. It uses the base-owned <see cref="EventAggregatorBase.SyncRoot"/> for all handler
    /// storage, exactly as a correct derived implementation must.
    /// </summary>
    private sealed class GatedAggregator : EventAggregatorBase
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = [];
        private readonly ManualResetEventSlim _release = new(false);
        private int _gateArmed = 1;

        public ManualResetEventSlim ForwardCallEntered { get; } = new(false);

        public void ReleaseForwardCall() => _release.Set();

        public override void Subscribe<TEvent>(Func<TEvent, Task> handler)
        {
            // Only the first forward call is gated; the removal path never routes through here.
            if (Interlocked.Exchange(ref _gateArmed, 0) == 1)
            {
                ForwardCallEntered.Set();
                _release.Wait(TimeSpan.FromSeconds(5));
            }

            lock (SyncRoot)
            {
                if (!_handlers.TryGetValue(typeof(TEvent), out var list))
                {
                    list = [];
                    _handlers[typeof(TEvent)] = list;
                }

                list.Add(handler);
            }
        }

        public override void Unsubscribe<TEvent>(Func<TEvent, Task> handler)
        {
            lock (SyncRoot)
            {
                if (_handlers.TryGetValue(typeof(TEvent), out var list))
                    list.Remove(handler);
            }
        }

        protected override void UnsubscribeWrappers<TEvent>(IReadOnlyCollection<Func<TEvent, Task>> wrappers)
        {
            var set = new HashSet<Func<TEvent, Task>>(wrappers);

            lock (SyncRoot)
            {
                if (_handlers.TryGetValue(typeof(TEvent), out var list))
                    list.RemoveAll(h => set.Contains((Func<TEvent, Task>)h));
            }
        }

        public override async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        {
            List<Delegate> snapshot;

            lock (SyncRoot)
            {
                if (!_handlers.TryGetValue(typeof(TEvent), out var list))
                    return;

                snapshot = [.. list];
            }

            foreach (var handler in snapshot)
                await ((Func<TEvent, Task>)handler)(@event).ConfigureAwait(false);
        }
    }
}
