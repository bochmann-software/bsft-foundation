using FluentAssertions;
using Xunit;

namespace Bsft.EventAggregator.Tests;

public class SimpleEventAggregatorTests
{
    private readonly SimpleEventAggregator _sut = new();

    public record TestEvent(string Message);
    public record OtherEvent(int Value);

    [Fact]
    public async Task PublishAsync_WithSubscriber_InvokesHandler()
    {
        string? received = null;
        Func<TestEvent, Task> handler = e =>
        {
            received = e.Message;
            return Task.CompletedTask;
        };

        _sut.Subscribe(handler);
        await _sut.PublishAsync(new TestEvent("hello"));

        received.Should().Be("hello");
    }

    [Fact]
    public async Task PublishAsync_WithNoSubscribers_DoesNotThrow()
    {
        var act = async () => await _sut.PublishAsync(new TestEvent("hello"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Unsubscribe_RemovesHandler()
    {
        var callCount = 0;
        Func<TestEvent, Task> handler = _ =>
        {
            callCount++;
            return Task.CompletedTask;
        };

        _sut.Subscribe(handler);
        await _sut.PublishAsync(new TestEvent("first"));

        _sut.Unsubscribe(handler);
        await _sut.PublishAsync(new TestEvent("second"));

        callCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_WithMultipleSubscribers_InvokesAll()
    {
        var results = new List<string>();
        Func<TestEvent, Task> handler1 = e =>
        {
            results.Add("handler1:" + e.Message);
            return Task.CompletedTask;
        };
        Func<TestEvent, Task> handler2 = e =>
        {
            results.Add("handler2:" + e.Message);
            return Task.CompletedTask;
        };

        _sut.Subscribe(handler1);
        _sut.Subscribe(handler2);
        await _sut.PublishAsync(new TestEvent("test"));

        results.Should().HaveCount(2);
        results.Should().Contain("handler1:test");
        results.Should().Contain("handler2:test");
    }

    [Fact]
    public async Task PublishAsync_InvokesHandlersSequentially()
    {
        var order = new List<int>();
        Func<TestEvent, Task> handler1 = async _ =>
        {
            // Yield instead of a wall-clock delay: if PublishAsync awaited handlers concurrently,
            // handler2 would record before handler1's continuation resumes. Sequential awaiting
            // guarantees handler1 finishes first, deterministically and without a timing dependency.
            await Task.Yield();
            order.Add(1);
        };
        Func<TestEvent, Task> handler2 = _ =>
        {
            order.Add(2);
            return Task.CompletedTask;
        };

        _sut.Subscribe(handler1);
        _sut.Subscribe(handler2);
        await _sut.PublishAsync(new TestEvent("test"));

        order.Should().ContainInOrder(1, 2);
    }

    [Fact]
    public async Task PublishAsync_SnapshotIteration_AllowsSubscribeDuringPublish()
    {
        var callCount = 0;
        Func<TestEvent, Task> newHandler = _ =>
        {
            callCount++;
            return Task.CompletedTask;
        };

        Func<TestEvent, Task> existingHandler = _ =>
        {
            _sut.Subscribe(newHandler);
            return Task.CompletedTask;
        };

        _sut.Subscribe(existingHandler);
        await _sut.PublishAsync(new TestEvent("test"));

        // The new handler should not have been called during this publish
        // because we iterate a snapshot
        callCount.Should().Be(0);

        // But it should be called on the next publish
        await _sut.PublishAsync(new TestEvent("test2"));
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_SnapshotIteration_AllowsUnsubscribeDuringPublish()
    {
        var callCount = 0;
        Func<TestEvent, Task>? handler2 = null;

        Func<TestEvent, Task> handler1 = _ =>
        {
            _sut.Unsubscribe(handler2!);
            return Task.CompletedTask;
        };

        handler2 = _ =>
        {
            callCount++;
            return Task.CompletedTask;
        };

        _sut.Subscribe(handler1);
        _sut.Subscribe(handler2);
        await _sut.PublishAsync(new TestEvent("test"));

        // handler2 should still be called because we iterate a snapshot
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_DifferentEventTypes_OnlyInvokesMatchingHandlers()
    {
        string? testResult = null;
        int? otherResult = null;

        _sut.Subscribe<TestEvent>(e =>
        {
            testResult = e.Message;
            return Task.CompletedTask;
        });
        _sut.Subscribe<OtherEvent>(e =>
        {
            otherResult = e.Value;
            return Task.CompletedTask;
        });

        await _sut.PublishAsync(new TestEvent("hello"));

        testResult.Should().Be("hello");
        otherResult.Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_WithCancellation_ThrowsOperationCanceled()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _sut.Subscribe<TestEvent>(_ => Task.CompletedTask);

        var act = async () => await _sut.PublishAsync(new TestEvent("test"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PublishAsync_CancelledAfterAHandlerThrew_ReportsBoth()
    {
        // A handler that already threw threw whether or not the publish was cancelled a moment later,
        // and a caller told only about the cancellation would never learn the event was half delivered.
        var cts = new CancellationTokenSource();
        var failure = new InvalidOperationException("first handler");

        _sut.Subscribe<TestEvent>(_ =>
        {
            cts.Cancel();

            throw failure;
        });
        _sut.Subscribe<TestEvent>(_ => Task.CompletedTask);

        var act = async () => await _sut.PublishAsync(new TestEvent("test"), cts.Token);

        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.InnerExceptions.Should().HaveCount(2);
        thrown.Which.InnerExceptions[0].Should().BeSameAs(failure);
        thrown.Which.InnerExceptions[1].Should().BeOfType<OperationCanceledException>();
    }

    [Fact]
    public void Subscribe_NullHandler_ThrowsArgumentNullException()
    {
        var act = () => _sut.Subscribe<TestEvent>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Unsubscribe_NullHandler_ThrowsArgumentNullException()
    {
        var act = () => _sut.Unsubscribe<TestEvent>((Func<TestEvent, Task>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Unsubscribe_NonExistentHandler_DoesNotThrow()
    {
        Func<TestEvent, Task> handler = _ => Task.CompletedTask;

        var act = () => _sut.Unsubscribe(handler);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task PublishAsync_NullEvent_ThrowsArgumentNullException()
    {
        // Exercises SimpleEventAggregator.PublishAsync line 52: null guard.
        var act = async () => await _sut.PublishAsync<TestEvent>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Subscribe_WithSynchronousAction_InvokesHandler()
    {
        // SimpleEventAggregator provides its own Action<TEvent> overload.
        IEventAggregator aggregator = _sut;
        string? received = null;

        aggregator.Subscribe<TestEvent>(e => { received = e.Message; });
        await aggregator.PublishAsync(new TestEvent("sync-hello"));

        received.Should().Be("sync-hello");
    }

    [Fact]
    public async Task Unsubscribe_WithSynchronousAction_RemovesHandler()
    {
        // Regression: Subscribe(Action) stores the wrapper so Unsubscribe(Action) can remove it.
        // Previously the wrapper identities diverged and Unsubscribe was a silent no-op.
        var callCount = 0;
        Action<TestEvent> handler = _ => callCount++;

        _sut.Subscribe(handler);
        await _sut.PublishAsync(new TestEvent("first"));

        _sut.Unsubscribe(handler);
        await _sut.PublishAsync(new TestEvent("second"));

        callCount.Should().Be(1);
    }

    [Fact]
    public async Task Unsubscribe_WithSynchronousAction_NotPreviouslySubscribed_DoesNotThrow()
    {
        Action<TestEvent> handler = _ => { };

        var act = () => _sut.Unsubscribe(handler);

        act.Should().NotThrow();
        await Task.CompletedTask;
    }

    [Fact]
    public void Subscribe_NullAction_ThrowsArgumentNullException()
    {
        var act = () => _sut.Subscribe<TestEvent>((Action<TestEvent>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Unsubscribe_NullAction_ThrowsArgumentNullException()
    {
        var act = () => _sut.Unsubscribe<TestEvent>((Action<TestEvent>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Publish_WithSynchronousAction_InvokesDefaultInterfaceMethod()
    {
        // Exercises IEventAggregator.Publish<TEvent>(TEvent @event) default interface method.
        IEventAggregator aggregator = _sut;
        string? received = null;

        aggregator.Subscribe<TestEvent>(e => { received = e.Message; });
        aggregator.Publish(new TestEvent("sync-publish"));

        received.Should().Be("sync-publish");
    }

    [Fact]
    public async Task PublishAsync_OneHandlerThrows_InvokesAllAndAggregatesFailures()
    {
        var invoked = new List<int>();
        _sut.Subscribe<TestEvent>(_ => invoked.Add(1));
        _sut.Subscribe<TestEvent>(_ => throw new InvalidOperationException("boom"));
        _sut.Subscribe<TestEvent>(_ => invoked.Add(3));

        var act = async () => await _sut.PublishAsync(new TestEvent("test"));

        var assertion = await act.Should().ThrowAsync<AggregateException>();
        assertion.Which.InnerExceptions.Should().ContainSingle()
            .Which.Should().BeOfType<InvalidOperationException>();
        // Subscribers before and after the faulting one are still invoked.
        invoked.Should().ContainInOrder(1, 3);
    }

    [Fact]
    public async Task Subscribe_SameActionTwice_UnsubscribeOnce_RemovesBothWrappers()
    {
        // Regression (2965): subscribing the same Action instance twice must register two wrappers,
        // and a single Unsubscribe must remove every wrapper the handler produced. Previously the
        // wrapper map overwrote, so one wrapper leaked and kept firing after Unsubscribe.
        var callCount = 0;
        Action<TestEvent> handler = _ => callCount++;

        _sut.Subscribe(handler);
        _sut.Subscribe(handler);
        await _sut.PublishAsync(new TestEvent("first"));

        // Both registrations fire.
        callCount.Should().Be(2);

        _sut.Unsubscribe(handler);
        await _sut.PublishAsync(new TestEvent("second"));

        // No wrapper is left behind: the count is unchanged after unsubscribing once.
        callCount.Should().Be(2);
    }

    [Fact]
    public void RequiredMembers_OnBareImplementation_MustBeImplemented()
    {
        // Proves the Action<TEvent> Subscribe/Unsubscribe overloads are required members (2963):
        // an implementation that supplies only the Func overloads would no longer compile, so this
        // bare aggregator implements all four and the sync overloads route to its own bodies rather
        // than to a defaulted forwarding wrapper. Publish remains a default interface method.
        IEventAggregator aggregator = new RequiredMembersEventAggregator();
        var subscribed = false;
        var unsubscribed = false;

        aggregator.Subscribe<TestEvent>(_ => subscribed = true);
        aggregator.Unsubscribe<TestEvent>(_ => unsubscribed = true);
        aggregator.Publish(new TestEvent("x"));

        subscribed.Should().BeTrue();
        unsubscribed.Should().BeTrue();
    }

    /// <summary>
    /// Minimal IEventAggregator that implements every required member (both Func and Action overloads)
    /// and invokes each registered handler once, so the sync-overload bodies and the Publish default
    /// interface method execute.
    /// </summary>
    private sealed class RequiredMembersEventAggregator : IEventAggregator
    {
        public void Subscribe<TEvent>(Func<TEvent, Task> handler) => handler(default!).GetAwaiter().GetResult();

        public void Unsubscribe<TEvent>(Func<TEvent, Task> handler) => handler(default!).GetAwaiter().GetResult();

        public void Subscribe<TEvent>(Action<TEvent> handler) => handler(default!);

        public void Unsubscribe<TEvent>(Action<TEvent> handler) => handler(default!);

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) => Task.CompletedTask;
    }
}
