using Bsft.Hosting.BackgroundWork.Pipelines;
using Bsft.Tests.Shared;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Xunit;

namespace Bsft.Hosting.BackgroundWork.Tests.Pipelines;

public sealed class OperationRacerTests
{
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly OperationRacer _racer;

    public OperationRacerTests() => _racer = new OperationRacer(_clock);

    [Fact]
    public async Task RunAsync_WhenTheOperationCompletes_ReturnsItsValue()
    {
        var outcome = await _racer.RunAsync(static _ => Task.FromResult(42), Timeout.InfiniteTimeSpan, CancellationToken.None);

        outcome.Should().Be(new RacedResult<int>(RacedStatus.Completed, 42, null));
    }

    [Fact]
    public async Task RunAsync_WhenTheOperationThrowsSynchronously_ReturnsTheFault()
    {
        var outcome = await _racer.RunAsync<int>(
            static _ => throw new InvalidOperationException("sync"),
            Timeout.InfiniteTimeSpan,
            CancellationToken.None);

        outcome.Status.Should().Be(RacedStatus.Faulted);
        outcome.Fault.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task RunAsync_WhenTheOperationIsCancelledByItself_ReturnsAFaultThatIsACancellation()
    {
        var outcome = await _racer.RunAsync<int>(
            static _ => Task.FromCanceled<int>(new CancellationToken(canceled: true)),
            Timeout.InfiniteTimeSpan,
            CancellationToken.None);

        outcome.Status.Should().Be(RacedStatus.Faulted);
        outcome.Fault.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_WhenTheLimitPassesFirst_GivesUpOnAnOperationThatIgnoresItsToken()
    {
        var neverFinishes = new TaskCompletionSource<int>();

        var racing = _racer.RunAsync(_ => neverFinishes.Task, TimeSpan.FromSeconds(5), CancellationToken.None);
        await _clock.WaitForTimerAsync(TimeSpan.FromSeconds(5));
        _clock.Advance(TimeSpan.FromSeconds(4));
        racing.IsCompleted.Should().BeFalse();
        _clock.Advance(TimeSpan.FromSeconds(1));

        (await racing).Status.Should().Be(RacedStatus.GaveUp);
    }

    [Fact]
    public async Task RunAsync_WhenTheOperationHonorsItsTokenAtTheLimit_ReportsGaveUpWhicheverFinishesFirst()
    {
        var racing = _racer.RunAsync<int>(
            async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 1;
            },
            TimeSpan.FromSeconds(5),
            CancellationToken.None);
        await _clock.WaitForTimerAsync(TimeSpan.FromSeconds(5));

        _clock.Advance(TimeSpan.FromSeconds(5));

        (await racing).Status.Should().Be(RacedStatus.GaveUp);
    }

    [Fact]
    public async Task RunAsync_WhenTheOperationHonorsItsTokenAtTheAbort_ReportsGaveUp()
    {
        using var abort = new CancellationTokenSource();
        var armed = new TaskCompletionSource();
        var racing = _racer.RunAsync<int>(
            async token =>
            {
                armed.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 1;
            },
            Timeout.InfiniteTimeSpan,
            abort.Token);
        await armed.Task;

        await abort.CancelAsync();

        (await racing).Status.Should().Be(RacedStatus.GaveUp);
    }

    [Fact]
    public async Task RunAsync_WhenTheAbortTokenIsCancelledFirst_GivesUpOnAnOperationThatIgnoresItsToken()
    {
        var neverFinishes = new TaskCompletionSource<int>();
        using var abort = new CancellationTokenSource();

        var racing = _racer.RunAsync(_ => neverFinishes.Task, Timeout.InfiniteTimeSpan, abort.Token);
        await abort.CancelAsync();

        (await racing).Status.Should().Be(RacedStatus.GaveUp);
    }

    [Fact]
    public async Task RunAsync_WhenTheAbortTokenIsAlreadyCancelled_DoesNotStartTheOperation()
    {
        var started = false;
        using var abort = new CancellationTokenSource();
        await abort.CancelAsync();

        var outcome = await _racer.RunAsync(
            _ =>
            {
                started = true;
                return Task.FromResult(1);
            },
            Timeout.InfiniteTimeSpan,
            abort.Token);

        outcome.Status.Should().Be(RacedStatus.GaveUp);
        started.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WhenAnAbandonedOperationFaultsLater_TheFaultDoesNotSurfaceAsUnobserved()
    {
        var unobservedControl = 0;
        var unobservedLate = 0;
        void Count(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            var messages = args.Exception.Flatten().InnerExceptions.Select(fault => fault.Message).ToArray();
            if (messages.Contains("control"))
                Interlocked.Increment(ref unobservedControl);
            if (messages.Contains("late"))
                Interlocked.Increment(ref unobservedLate);
        }

        TaskScheduler.UnobservedTaskException += Count;
        try
        {
            // Nothing may root the tasks once these return, or the finalizers cannot run.
            CreateUnobservedFault();
            await AbandonAndFaultLateAsync();
            // Finalization timing is the runtime's: collect until the control has been reported rather than
            // assuming a fixed number of passes is enough on every runtime and platform.
            for (var pass = 0; pass < 100 && Volatile.Read(ref unobservedControl) == 0; pass++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(10);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();

            // The control proves finalizers ran at all, which makes the zero below mean something.
            unobservedControl.Should().Be(1);
            unobservedLate.Should().Be(0);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Count;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateUnobservedFault() => _ = Task.FromException(new InvalidOperationException("control"));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task AbandonAndFaultLateAsync()
    {
        var late = new TaskCompletionSource<int>();
        var started = new TaskCompletionSource();
        using var abort = new CancellationTokenSource();
        var racing = _racer.RunAsync(
            _ =>
            {
                started.SetResult();
                return late.Task;
            },
            Timeout.InfiniteTimeSpan,
            abort.Token);

        // The operation has to be running before it is abandoned: on a busy thread pool it may not have
        // started yet, and an operation that never started leaves its own task for nobody to observe, which
        // is the test's doing and not the racer's.
        await started.Task;
        await abort.CancelAsync();
        (await racing).Status.Should().Be(RacedStatus.GaveUp);
        late.SetException(new InvalidOperationException("late"));
    }

    [Fact]
    public void OperationRacer_WhenTheClockIsNull_Throws()
    {
        var act = () => new OperationRacer(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_WhenTheOperationIsNull_Throws()
    {
        var act = () => _racer.RunAsync<int>(null!, Timeout.InfiniteTimeSpan, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_WhenTheOperationCancelsOnItsOwnToken_KeepsTheRealException()
    {
        var outcome = await _racer.RunAsync<int>(
            static async _ =>
            {
                await Task.Yield();
                throw new TaskCanceledException("the http call timed out");
            },
            Timeout.InfiniteTimeSpan,
            CancellationToken.None);

        outcome.Status.Should().Be(RacedStatus.Faulted);
        outcome.Fault.Should().BeOfType<TaskCanceledException>().Which.Message.Should().Be("the http call timed out");
    }
}
