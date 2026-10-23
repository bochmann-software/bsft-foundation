using Bsft.Core.Observers;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests.Observers;

public sealed class ObserverCompositeTests
{
    private readonly List<string> _told = [];
    private readonly ObserverGuard _guard = new(new SilentReporter());

    [Fact]
    public void Notify_TellsEveryObserverInRegistrationOrder()
    {
        var composite = Composite(new Observer("a", _told), new Observer("b", _told), new Observer("c", _told));

        composite.Notify(observer => observer.Heard("event"));

        _told.Should().Equal("a:event", "b:event", "c:event");
    }

    [Fact]
    public void Notify_WhenAnObserverThrows_TheOnesAfterItStillHear()
    {
        var composite = Composite(new Observer("a", _told), new Observer("b", _told) { Fails = true }, new Observer("c", _told));

        composite.Notify(observer => observer.Heard("event"));

        _told.Should().Equal("a:event", "c:event");
        _guard.Failures.Should().Be(1);
    }

    [Fact]
    public void Notify_WithoutObservers_DoesNothing()
    {
        var composite = Composite();

        composite.Notify(observer => observer.Heard("event"));

        composite.Observers.Should().BeEmpty();
        _told.Should().BeEmpty();
    }

    [Fact]
    public void Observers_AreASnapshotOfWhatWasRegistered()
    {
        var registered = new List<Observer> { new("a", _told) };
        var composite = new ObserverComposite<Observer>(registered, _guard);

        registered.Add(new Observer("late", _told));
        composite.Notify(observer => observer.Heard("event"));

        composite.Observers.Should().HaveCount(1);
        _told.Should().Equal("a:event");
    }

    [Fact]
    public async Task NotifyAsync_TellsEveryObserverOneAfterTheOther()
    {
        var composite = Composite(new Observer("a", _told), new Observer("b", _told));

        await composite.NotifyAsync((observer, token) => observer.HeardAsync("event", token));

        _told.Should().Equal("a:event", "b:event");
    }

    [Fact]
    public async Task NotifyAsync_WhenAnObserverFaults_TheOnesAfterItStillHear()
    {
        var composite = Composite(new Observer("a", _told) { Fails = true }, new Observer("b", _told));

        await composite.NotifyAsync((observer, token) => observer.HeardAsync("event", token));

        _told.Should().Equal("b:event");
        _guard.Failures.Should().Be(1);
    }

    [Fact]
    public async Task NotifyAsync_WhenTheOperationIsCancelled_StopsTellingAndThrows()
    {
        using var source = new CancellationTokenSource();
        var composite = Composite(new Observer("a", _told), new Observer("b", _told));
        await source.CancelAsync();

        await FluentActions.Awaiting(() => composite.NotifyAsync((observer, token) => observer.HeardAsync("event", token), source.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        _told.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyAsync_WhenTheOperationIsCancelledDuringAnObserverThatIgnoresTheToken_NoOneAfterItIsTold()
    {
        using var source = new CancellationTokenSource();
        var composite = Composite(new Observer("a", _told), new Observer("b", _told));

        await FluentActions.Awaiting(() => composite.NotifyAsync(
                async (observer, _) =>
                {
                    // The observer neither looks at its token nor fails; the operation is cancelled meanwhile.
                    observer.Heard("event");
                    await source.CancelAsync();
                },
                source.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        _told.Should().Equal("a:event");
    }

    [Fact]
    public async Task Arguments_AreValidated()
    {
        var composite = Composite(new Observer("a", _told));

        FluentActions.Invoking(() => new ObserverComposite<Observer>(null!, _guard)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ObserverComposite<Observer>([], null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => composite.Notify(null!)).Should().Throw<ArgumentNullException>();
        await FluentActions.Awaiting(() => composite.NotifyAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    private ObserverComposite<Observer> Composite(params Observer[] observers) => new(observers, _guard);

    private sealed class Observer(string name, List<string> told)
    {
        internal bool Fails { get; init; }

        internal void Heard(string message)
        {
            if (Fails)
                throw new InvalidOperationException($"{name} failed");

            told.Add($"{name}:{message}");
        }

        internal Task HeardAsync(string message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Heard(message);
            return Task.CompletedTask;
        }
    }

    private sealed class SilentReporter : IObserverFailureReporter
    {
        public void ReportObserverFailed(object observer, Exception failure)
        {
        }
    }
}
