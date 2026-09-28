using FluentAssertions;
using Xunit;

namespace Bsft.EventAggregator.Tests;

public class EventAggregatorBaseTests
{
    public record TestEvent(string Message);

    private readonly TestAggregator _sut = new();

    [Fact]
    public async Task Subscribe_WithSynchronousAction_InvokesHandler_WithoutOwnWrapperBookkeeping()
    {
        // The derived aggregator supplies only the Func-based members and a bulk-remove seam; the base
        // provides the Action-overload wrapper bookkeeping for free.
        string? received = null;

        _sut.Subscribe<TestEvent>(e => received = e.Message);
        await _sut.PublishAsync(new TestEvent("hello"));

        received.Should().Be("hello");
    }

    [Fact]
    public async Task Unsubscribe_WithSynchronousAction_RemovesHandler()
    {
        var callCount = 0;
        Action<TestEvent> handler = _ => callCount++;

        _sut.Subscribe(handler);
        await _sut.PublishAsync(new TestEvent("first"));

        _sut.Unsubscribe(handler);
        await _sut.PublishAsync(new TestEvent("second"));

        callCount.Should().Be(1);
    }

    [Fact]
    public async Task Subscribe_SameActionTwice_UnsubscribeOnce_RemovesBothWrappers()
    {
        // Deriving from EventAggregatorBase inherits the duplicate-subscribe-remove-both semantics.
        var callCount = 0;
        Action<TestEvent> handler = _ => callCount++;

        _sut.Subscribe(handler);
        _sut.Subscribe(handler);
        await _sut.PublishAsync(new TestEvent("first"));

        callCount.Should().Be(2);

        _sut.Unsubscribe(handler);
        await _sut.PublishAsync(new TestEvent("second"));

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Unsubscribe_MultipleWrappers_RemovesAllInSinglePass()
    {
        // Behavioural proof of the single-pass multi-wrapper unsubscribe path: subscribing the same
        // Action three times records three wrappers, all of which must be gone after one Unsubscribe.
        var callCount = 0;
        Action<TestEvent> handler = _ => callCount++;

        _sut.Subscribe(handler);
        _sut.Subscribe(handler);
        _sut.Subscribe(handler);
        await _sut.PublishAsync(new TestEvent("first"));

        callCount.Should().Be(3);
        _sut.RemoveCalls.Should().Be(0);

        _sut.Unsubscribe(handler);

        // The base collected all recorded wrappers and invoked the bulk-remove seam exactly once.
        _sut.RemoveCalls.Should().Be(1);
        _sut.LastRemovedCount.Should().Be(3);

        await _sut.PublishAsync(new TestEvent("second"));
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task Unsubscribe_NeverSubscribedAction_IsSafeNoOp()
    {
        Action<TestEvent> handler = _ => { };

        var act = () => _sut.Unsubscribe(handler);

        act.Should().NotThrow();
        _sut.RemoveCalls.Should().Be(0);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Minimal aggregator that derives from <see cref="EventAggregatorBase"/> and writes no wrapper
    /// bookkeeping of its own: it only stores the Func handlers, removes them in one locked pass, and
    /// records how often the bulk-remove seam was called so the single-pass contract is observable.
    /// </summary>
    private sealed class TestAggregator : EventAggregatorBase
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = [];

        public int RemoveCalls { get; private set; }

        public int LastRemovedCount { get; private set; }

        public override void Subscribe<TEvent>(Func<TEvent, Task> handler)
        {
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
            RemoveCalls++;
            LastRemovedCount = wrappers.Count;

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
