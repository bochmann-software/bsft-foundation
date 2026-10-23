using Bsft.Core.Observers;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests.Observers;

public sealed class ObserverGuardTests
{
    private readonly RecordingReporter _reporter = new();
    private readonly ObserverGuard _guard;

    public ObserverGuardTests() => _guard = new ObserverGuard(_reporter);

    [Fact]
    public void Invoke_RunsTheCall()
    {
        var ran = false;

        _guard.Invoke(this, () => ran = true);

        ran.Should().BeTrue();
        _guard.Failures.Should().Be(0);
        _reporter.Reports.Should().BeEmpty();
    }

    [Fact]
    public void Invoke_ContainsAThrowCountsItAndReportsIt()
    {
        var failure = new InvalidOperationException("observer failed");

        _guard.Invoke(this, () => throw failure);

        _guard.Failures.Should().Be(1);
        _reporter.Reports.Should().ContainSingle().Which.Should().Be((this, failure));
    }

    [Fact]
    public void Invoke_WhenTheReporterThrows_StillContainsTheFailureAndCountsIt()
    {
        var guard = new ObserverGuard(new ThrowingReporter());

        guard.Invoke(this, () => throw new InvalidOperationException("observer failed"));

        guard.Failures.Should().Be(1);
        guard.ReporterFailures.Should().Be(1);
    }

    [Fact]
    public void Invoke_WhenTheReporterWorks_CountsNoReporterFailure()
    {
        _guard.Invoke(this, () => throw new InvalidOperationException("observer failed"));

        _guard.ReporterFailures.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheReporterThrows_CountsTheReporterFailure()
    {
        var guard = new ObserverGuard(new ThrowingReporter());

        await guard.InvokeAsync(this, _ => throw new InvalidOperationException("observer failed"));
        await guard.InvokeAsync(this, _ => throw new InvalidOperationException("observer failed"));

        guard.Failures.Should().Be(2);
        guard.ReporterFailures.Should().Be(2);
    }

    [Fact]
    public async Task InvokeAsync_RunsTheCallWithTheToken()
    {
        using var source = new CancellationTokenSource();
        CancellationToken seen = default;

        await _guard.InvokeAsync(this, token =>
        {
            seen = token;
            return Task.CompletedTask;
        }, source.Token);

        seen.Should().Be(source.Token);
        _guard.Failures.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_ContainsASynchronousThrow()
    {
        await _guard.InvokeAsync(this, _ => throw new InvalidOperationException("observer failed"));

        _guard.Failures.Should().Be(1);
        _reporter.Reports.Should().ContainSingle();
    }

    [Fact]
    public async Task InvokeAsync_ContainsAFaultedTask()
    {
        await _guard.InvokeAsync(this, _ => Task.FromException(new InvalidOperationException("observer failed")));

        _guard.Failures.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_ContainsACancellationTheObserverRaisedOnItsOwn()
    {
        using var source = new CancellationTokenSource();

        await _guard.InvokeAsync(this, _ => throw new OperationCanceledException(), source.Token);

        _guard.Failures.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheOperationIsCancelled_DoesNotContainIt()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await FluentActions.Awaiting(() => _guard.InvokeAsync(this, token => Task.FromCanceled(token), source.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        _guard.Failures.Should().Be(0);
    }

    [Fact]
    public async Task Arguments_AreValidated()
    {
        FluentActions.Invoking(() => _guard.Invoke(null!, () => { })).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => _guard.Invoke(this, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ObserverGuard(null!)).Should().Throw<ArgumentNullException>();
        await FluentActions.Awaiting(() => _guard.InvokeAsync(null!, _ => Task.CompletedTask)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => _guard.InvokeAsync(this, null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    private sealed class RecordingReporter : IObserverFailureReporter
    {
        internal List<(object Observer, Exception Failure)> Reports { get; } = [];

        public void ReportObserverFailed(object observer, Exception failure) => Reports.Add((observer, failure));
    }

    private sealed class ThrowingReporter : IObserverFailureReporter
    {
        public void ReportObserverFailed(object observer, Exception failure) => throw new InvalidOperationException("the reporter failed too");
    }
}
