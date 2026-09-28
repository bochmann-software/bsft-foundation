using Bsft.Core.Polling;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests.Polling;

public sealed class PollLoopTests : IDisposable
{
    private static readonly TimeSpan ShortInterval = TimeSpan.FromMilliseconds(1);

    // Long enough that a loop which waits cannot possibly run a second pass within the test, so a
    // second pass proves the wait was skipped rather than merely short.
    private static readonly TimeSpan EndlessInterval = TimeSpan.FromMinutes(10);

    private readonly RecordingReporter _reporter = new();
    private readonly CancellationTokenSource _stopping = new();

    private int _passes;

    public void Dispose() => _stopping.Dispose();

    [Fact]
    public async Task The_loop_runs_no_pass_at_all_when_it_starts_out_cancelled()
    {
        await _stopping.CancelAsync();

        await RunAsync(_ => FinishAsync(PollPassOutcome.PollAgain), ShortInterval);

        _passes.Should().Be(0);
        _reporter.Failures.Should().BeEmpty();
        _reporter.CancelledWithoutStop.Should().Be(0);
        _reporter.Stopping.Should().Be(0);
    }

    [Fact]
    public async Task A_faulted_pass_is_reported_and_the_loop_keeps_running()
    {
        // The whole point of the loop: a store that is unreachable must not take the service down.
        await RunAsync(_ => ThrowAsync(new InvalidOperationException("the store is unreachable")), ShortInterval);

        _passes.Should().BeGreaterThanOrEqualTo(3);
        _reporter.Failures.Should().HaveCountGreaterThanOrEqualTo(3);
        _reporter.CancelledWithoutStop.Should().Be(0);
        _reporter.Stopping.Should().Be(1);
    }

    [Fact]
    public async Task A_pass_that_failed_once_is_reported_as_that_failure_itself()
    {
        // What the service logs is the failure itself, not an AggregateException tree around it: an
        // alert rule keyed on the exception type has to see the type the pass actually threw.
        var failure = new InvalidOperationException("the store is unreachable");

        await RunAsync(_ => ThrowAsync(failure), ShortInterval);

        _reporter.Failures[0].Should().BeSameAs(failure);
    }

    [Fact]
    public async Task A_pass_that_failed_more_than_once_is_reported_as_the_flattened_aggregate()
    {
        // Nothing may be dropped when there is more than one failure, so the aggregate stands. It is
        // flattened, because the nesting says nothing the service could act on.
        var first = new InvalidOperationException("the store is unreachable");
        var second = new InvalidOperationException("and the transport is down");

        await RunAsync(_ => FailWithBoth(first, second), ShortInterval);

        _reporter.Failures[0].Should().BeOfType<AggregateException>()
            .Which.InnerExceptions.Should().BeEquivalentTo([first, second], o => o.WithoutStrictOrdering());
    }

    [Fact]
    public async Task A_pass_that_throws_before_it_returns_a_task_is_reported_and_the_loop_keeps_running()
    {
        // A pass is free to be a plain lambda that validates its arguments up front. Its throw never
        // reaches a task, so it can only be caught around the call — and the loop's whole promise is
        // that a failing pass does not take the host down.
        var failure = new InvalidOperationException("the pass was configured wrong");

        await RunAsync(_ => throw failure, ShortInterval);

        _passes.Should().BeGreaterThanOrEqualTo(3);
        _reporter.Failures.Should().HaveCountGreaterThanOrEqualTo(3);
        _reporter.Failures[0].Should().BeSameAs(failure);
        _reporter.CancelledWithoutStop.Should().Be(0);
        _reporter.Stopping.Should().Be(1);
    }

    [Fact]
    public async Task A_pass_that_throws_a_cancellation_before_it_returns_a_task_counts_as_cancelled_without_a_stop()
    {
        // Same shape, but the pass threw a timeout rather than a failure. Whether that cancellation
        // came out of a task or out of the call itself must not change what the service is told.
        using var passTimeout = new CancellationTokenSource();
        await passTimeout.CancelAsync();

        await RunAsync(
            _ => throw new OperationCanceledException("the command timed out", passTimeout.Token),
            ShortInterval);

        _passes.Should().BeGreaterThanOrEqualTo(3);
        _reporter.CancelledWithoutStop.Should().BeGreaterThanOrEqualTo(2);
        _reporter.Failures.Should().BeEmpty();
        _reporter.Stopping.Should().Be(1);
    }

    [Fact]
    public async Task A_pass_that_throws_a_cancellation_carrying_no_token_counts_as_cancelled_without_a_stop()
    {
        // Not every cancellation names the token it came from, and one that names none certainly did
        // not come from the stopping token, so it is the timeout case as well.
        await RunAsync(_ => throw new OperationCanceledException("the command timed out"), ShortInterval);

        _passes.Should().BeGreaterThanOrEqualTo(3);
        _reporter.CancelledWithoutStop.Should().BeGreaterThanOrEqualTo(2);
        _reporter.Failures.Should().BeEmpty();
        _reporter.Stopping.Should().Be(1);
    }

    [Fact]
    public async Task A_pass_that_returns_no_task_is_reported_and_the_loop_keeps_running()
    {
        // A delegate returning null would make the loop itself throw, out of a Task.WhenAny that no
        // catch here covers. It is the pass that is broken, so it is reported as a failed pass.
        await RunAsync(_ => null!, ShortInterval);

        _passes.Should().BeGreaterThanOrEqualTo(3);
        _reporter.Failures.Should().HaveCountGreaterThanOrEqualTo(3);
        _reporter.Failures[0].Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("null");
        _reporter.CancelledWithoutStop.Should().Be(0);
        _reporter.Stopping.Should().Be(1);
    }

    [Fact]
    public async Task A_pass_cancelled_without_a_stop_request_is_reported_and_the_loop_keeps_running()
    {
        // The command-timeout shape. Read as a shutdown it would end the loop, and the host would keep
        // running with this service no longer doing its work.
        using var passTimeout = new CancellationTokenSource();
        await passTimeout.CancelAsync();

        await RunAsync(
            _ => ThrowAsync(new OperationCanceledException("the command timed out", passTimeout.Token)),
            ShortInterval);

        _passes.Should().BeGreaterThanOrEqualTo(3);
        _reporter.CancelledWithoutStop.Should().BeGreaterThanOrEqualTo(2);
        _reporter.Failures.Should().BeEmpty();
        _reporter.Stopping.Should().Be(1);
    }

    [Fact]
    public async Task A_pass_cancelled_by_the_stop_request_ends_the_loop_without_throwing()
    {
        var act = async () => await RunAsync(
            _ => ThrowAsync(new OperationCanceledException("the host is stopping", _stopping.Token)),
            ShortInterval,
            stopAfter: 1);

        await act.Should().NotThrowAsync();
        _passes.Should().Be(1);
        _reporter.Stopping.Should().Be(1);
        _reporter.Failures.Should().BeEmpty();
        _reporter.CancelledWithoutStop.Should().Be(0);
    }

    [Fact]
    public async Task A_pass_that_left_work_behind_polls_again_without_waiting_for_the_interval()
    {
        // The outbox contract: a full batch means more is pending, so the dispatcher does not back off
        // until it has run dry. With an interval of ten minutes, a third pass can only happen when the
        // wait was skipped.
        await RunAsync(_ => FinishAsync(PollPassOutcome.PollAgain), EndlessInterval);

        _passes.Should().Be(3);

        // Nothing is reported: the stop was seen at the top of the loop rather than surfaced out of a
        // pass or a wait, so there was no cancellation to explain.
        _reporter.Stopping.Should().Be(0);
    }

    [Fact]
    public async Task A_pass_that_asked_to_wait_does_not_run_again_before_the_interval_is_over()
    {
        // The mirror image, and the pruner's contract: the loop parks in the wait after the first pass
        // and only leaves it when the stopping token is cancelled, so exactly one pass ran.
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var loop = RunAsync(
            async _ =>
            {
                var outcome = await FinishAsync(PollPassOutcome.WaitForInterval);
                finished.TrySetResult();

                return outcome;
            },
            EndlessInterval,
            stopAfter: int.MaxValue);

        // Stopped once the pass has handed back its outcome, which is all the loop needs to go into the
        // wait, rather than after a duration it is hoped to have got there in. WhenAny against the loop
        // so a loop that ended early fails this test instead of hanging it.
        await Task.WhenAny(finished.Task, loop);
        finished.Task.IsCompletedSuccessfully.Should().BeTrue();

        await _stopping.CancelAsync();
        await loop;

        _passes.Should().Be(1);
        _reporter.Stopping.Should().Be(1);
    }

    [Fact]
    public void The_loop_rejects_a_missing_pass_or_reporter()
    {
        var withoutPass = () => new PollLoop(null!, _reporter, ShortInterval);
        var withoutReporter = () => new PollLoop(_ => FinishAsync(PollPassOutcome.PollAgain), null!, ShortInterval);

        withoutPass.Should().Throw<ArgumentNullException>().WithParameterName("pass");
        withoutReporter.Should().Throw<ArgumentNullException>().WithParameterName("reporter");
    }

    /// <summary>
    /// Runs the loop over <paramref name="pass"/>, counting the passes and asking for the stop once
    /// <paramref name="stopAfter"/> of them have run, so every test ends on its own.
    /// </summary>
    private Task RunAsync(
        Func<CancellationToken, Task<PollPassOutcome>> pass,
        TimeSpan interval,
        int stopAfter = 3) =>
        new PollLoop(
            ct =>
            {
                _passes++;

                if (_passes >= stopAfter)
                    _stopping.Cancel();

                return pass(ct);
            },
            _reporter,
            interval).RunAsync(_stopping.Token);

    /// <summary>
    /// A pass that ends in <paramref name="failure"/>. It is thrown out of an async method rather than
    /// handed to <see cref="Task.FromException(Exception)"/>, because that is what makes a task that
    /// ended in an <see cref="OperationCanceledException"/> cancelled rather than faulted — the very
    /// distinction the loop is built on.
    /// </summary>
    private static async Task<PollPassOutcome> ThrowAsync(Exception failure)
    {
        await Task.Yield();

        throw failure;
    }

    /// <summary>
    /// A pass that ends in two failures at once, which is what a pass running its work concurrently
    /// produces. Only <see cref="Task.WhenAll(Task[])"/> faults a task with more than one exception.
    /// </summary>
    /// <remarks>
    /// The task is built rather than awaited out of an async method, because awaiting a
    /// <see cref="Task.WhenAll(Task[])"/> rethrows only its first exception and the resulting task
    /// would carry one failure again — the very case this stands in contrast to. What the loop sees
    /// is what <see cref="Task.WhenAll(Task[])"/> leaves behind: two failures under one aggregate.
    /// </remarks>
    private static Task<PollPassOutcome> FailWithBoth(Exception first, Exception second) =>
        Task.FromException<PollPassOutcome>(new AggregateException(first, second));

    private static async Task<PollPassOutcome> FinishAsync(PollPassOutcome outcome)
    {
        await Task.Yield();

        return outcome;
    }

    private sealed class RecordingReporter : IPollReporter
    {
        public List<Exception> Failures { get; } = [];

        public int CancelledWithoutStop { get; private set; }

        public int Stopping { get; private set; }

        public void ReportPassFailed(Exception failure) => Failures.Add(failure);

        public void ReportPassCanceledWithoutStop() => CancelledWithoutStop++;

        public void ReportStopping() => Stopping++;
    }
}
