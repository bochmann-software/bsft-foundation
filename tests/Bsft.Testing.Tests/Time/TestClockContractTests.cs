using Bsft.Testing.Time;
using FluentAssertions;
using Xunit;

namespace Bsft.Testing.Tests.Time;

/// <summary>The contract of <see cref="TestClock"/> that code written against a real clock relies on.</summary>
public class TestClockContractTests
{
    [Fact]
    public void CreateTimer_WithAZeroDueTime_FiresBeforeTheCallReturns()
    {
        var clock = new TestClock();
        var fired = 0;

        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        fired.Should().Be(1);
        clock.GetUtcNow().Should().Be(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Change_ToAZeroDueTime_FiresBeforeTheCallReturns()
    {
        var clock = new TestClock();
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromHours(1), Timeout.InfiniteTimeSpan);

        timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan).Should().BeTrue();

        fired.Should().Be(1);
    }

    [Fact]
    public void CreateTimer_WithAZeroDueTimeAndAPeriod_FiresOnceNowAndThenPerPeriod()
    {
        var clock = new TestClock();
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        clock.Advance(TimeSpan.FromSeconds(25));

        fired.Should().Be(3);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-1000)]
    public void CreateTimer_WithANegativeDueTime_Throws(int milliseconds)
    {
        var clock = new TestClock();

        var create = () => clock.CreateTimer(_ => { }, null, TimeSpan.FromMilliseconds(milliseconds), Timeout.InfiniteTimeSpan);

        create.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("dueTime");
    }

    [Fact]
    public void CreateTimer_WithANegativePeriod_Throws()
    {
        var clock = new TestClock();

        var create = () => clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-5));

        create.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("period");
    }

    [Fact]
    public void Change_WithANegativeDueTime_ThrowsAndLeavesTheTimerAsItWas()
    {
        var clock = new TestClock();
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);

        var change = () => timer.Change(TimeSpan.FromSeconds(-3), Timeout.InfiniteTimeSpan);

        change.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("dueTime");
        clock.Advance(TimeSpan.FromSeconds(10));
        fired.Should().Be(1);
    }

    [Fact]
    public void Change_AfterDispose_ReturnsFalseAndNeverFires()
    {
        var clock = new TestClock();
        var fired = 0;
        var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        timer.Dispose();

        var changed = timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        clock.Advance(TimeSpan.FromHours(1));

        changed.Should().BeFalse();
        fired.Should().Be(0);
        clock.PendingTimerCount.Should().Be(0);
    }

    [Fact]
    public void Advance_FromACallback_AddsToTheMoveItIsPartOf()
    {
        var clock = new TestClock();
        using var timer = clock.CreateTimer(
            _ => clock.Advance(TimeSpan.FromSeconds(10)), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(20));

        clock.GetUtcNow().Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(30));
    }

    [Fact]
    public async Task Advance_FromEightThreads_AddsUpExactly()
    {
        var clock = new TestClock();
        var start = clock.GetTimestamp();
        using var barrier = new Barrier(8);

        var threads = Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < 20000; i++)
                clock.Advance(TimeSpan.FromSeconds(1));
        }, TaskCreationOptions.LongRunning)).ToArray();
        await Task.WhenAll(threads);

        clock.GetUtcNow().Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(8 * 20000));
        clock.GetElapsedTime(start).Should().Be(TimeSpan.FromSeconds(8 * 20000));
    }

    [Fact]
    public async Task Advance_FromThreadsWhileTimersFire_NeverReadsAnEarlierInstant()
    {
        var clock = new TestClock();
        var last = DateTimeOffset.MinValue;
        var regressions = 0;
        var gate = new object();
        using var timer = clock.CreateTimer(
            _ =>
            {
                lock (gate)
                {
                    var now = clock.GetUtcNow();
                    if (now < last)
                        regressions++;

                    last = now;
                }
            },
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
        using var barrier = new Barrier(4);

        var threads = Enumerable.Range(0, 4).Select(_ => Task.Factory.StartNew(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < 2000; i++)
                clock.Advance(TimeSpan.FromSeconds(1));
        }, TaskCreationOptions.LongRunning)).ToArray();
        await Task.WhenAll(threads);

        clock.GetUtcNow().Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(8000));
        regressions.Should().Be(0);
    }

    [Fact]
    public async Task SetUtcNow_FromSeveralThreads_LeavesTheClockAtOneOfTheInstantsSet()
    {
        var clock = new TestClock();
        var instants = Enumerable.Range(1, 8).Select(i => DateTimeOffset.UnixEpoch.AddDays(i)).ToArray();
        using var barrier = new Barrier(8);

        var threads = instants.Select(instant => Task.Factory.StartNew(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < 2000; i++)
                clock.SetUtcNow(instant);
        }, TaskCreationOptions.LongRunning)).ToArray();
        await Task.WhenAll(threads);

        var settled = clock.GetUtcNow();
        instants.Should().Contain(settled);
        clock.Advance(TimeSpan.FromSeconds(1));
        clock.GetUtcNow().Should().Be(settled.AddSeconds(1));
    }

    [Fact]
    public void SetUtcNow_ThenAdvance_AddsToTheInstantSet()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch.AddDays(10));

        clock.SetUtcNow(DateTimeOffset.UnixEpoch.AddDays(2));
        clock.Advance(TimeSpan.FromHours(1));

        clock.GetUtcNow().Should().Be(DateTimeOffset.UnixEpoch.AddDays(2).AddHours(1));
    }

    [Fact]
    public void Timer_WhoseCallbackSchedulesAnother_FiresTheOtherInTheSameMoveAtItsDueTime()
    {
        var clock = new TestClock();
        var order = new List<string>();
        ITimer? inner = null;
        using var outer = clock.CreateTimer(
            _ =>
            {
                order.Add("outer");
                inner = clock.CreateTimer(
                    _ => order.Add("inner@" + (clock.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalSeconds),
                    null,
                    TimeSpan.FromSeconds(2),
                    Timeout.InfiniteTimeSpan);
            },
            null,
            TimeSpan.FromSeconds(3),
            Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(10));
        inner!.Dispose();

        order.Should().Equal("outer", "inner@5");
        clock.GetUtcNow().Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(10));
    }

    [Fact]
    public void GetTimestamp_FollowsTheClockForward()
    {
        var clock = new TestClock();
        var start = clock.GetTimestamp();

        clock.Advance(TimeSpan.FromSeconds(7));

        clock.GetElapsedTime(start).Should().Be(TimeSpan.FromSeconds(7));
        clock.TimestampFrequency.Should().Be(TimeSpan.TicksPerSecond);
    }

    [Fact]
    public void GetTimestamp_AfterABackwardSetUtcNow_NeverGoesBackAndGrowsWithTheNextMove()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch.AddDays(10));
        clock.Advance(TimeSpan.FromSeconds(30));
        var before = clock.GetTimestamp();

        clock.SetUtcNow(DateTimeOffset.UnixEpoch);
        var afterBackward = clock.GetTimestamp();
        clock.Advance(TimeSpan.FromSeconds(5));

        afterBackward.Should().Be(before);
        clock.GetElapsedTime(afterBackward).Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetTimestamp_AfterAForwardSetUtcNow_GrowsByTheDistance()
    {
        var clock = new TestClock();
        var start = clock.GetTimestamp();

        clock.SetUtcNow(DateTimeOffset.UnixEpoch.AddMinutes(3));

        clock.GetElapsedTime(start).Should().Be(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void GetElapsedTime_DeadlineMeasuredFromATimestamp_EndsWhenTheTestAdvancesPastIt()
    {
        var clock = new TestClock();
        var started = clock.GetTimestamp();
        var polls = 0;
        using var ticker = clock.CreateTimer(_ => polls++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        clock.Advance(TimeSpan.FromSeconds(14));
        var beforeDeadline = clock.GetElapsedTime(started) < TimeSpan.FromSeconds(15);
        clock.Advance(TimeSpan.FromSeconds(1));

        beforeDeadline.Should().BeTrue();
        (clock.GetElapsedTime(started) >= TimeSpan.FromSeconds(15)).Should().BeTrue();
        polls.Should().Be(15);
    }

    [Fact]
    public void Advance_WhenACallbackThrows_ReachesTheTargetFiresLaterTimersAndThenSurfacesTheException()
    {
        var clock = new TestClock();
        var laterFired = 0;
        using var throwing = clock.CreateTimer(
            _ => throw new InvalidOperationException("boom"), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        using var later = clock.CreateTimer(_ => laterFired++, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        var advance = () => clock.Advance(TimeSpan.FromSeconds(10));

        advance.Should().Throw<InvalidOperationException>().WithMessage("boom");
        laterFired.Should().Be(1);
        clock.GetUtcNow().Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(10));
    }

    [Fact]
    public void Advance_WhenSeveralCallbacksThrow_SurfacesAnAggregate()
    {
        var clock = new TestClock();
        using var first = clock.CreateTimer(
            _ => throw new InvalidOperationException("one"), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        using var second = clock.CreateTimer(
            _ => throw new ArgumentException("two"), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        var advance = () => clock.Advance(TimeSpan.FromSeconds(3));

        advance.Should().Throw<AggregateException>().Which.InnerExceptions.Should().HaveCount(2);
        clock.GetUtcNow().Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(3));
    }

    [Fact]
    public void CreateTimer_WithAZeroDueTimeThatThrows_SurfacesFromTheCreatingCall()
    {
        var clock = new TestClock();

        var create = () => clock.CreateTimer(
            _ => throw new InvalidOperationException("now"), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        create.Should().Throw<InvalidOperationException>().WithMessage("now");
    }

    [Fact]
    public async Task WaitForAnyTimerAsync_CompletesOnceATimerIsArmed()
    {
        var clock = new TestClock();
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var armed = clock.WaitForAnyTimerAsync(guard.Token);
        armed.IsCompleted.Should().BeFalse();

        using var timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        await armed;
        clock.PendingTimerCount.Should().Be(1);
        await clock.WaitForAnyTimerAsync(guard.Token);
    }

    [Fact]
    public async Task WaitForTimerAsync_CompletesOnlyForATimerDueExactlyThatFarAhead()
    {
        var clock = new TestClock();
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var other = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(9), Timeout.InfiniteTimeSpan);
        var armed = clock.WaitForTimerAsync(TimeSpan.FromSeconds(5), guard.Token);
        armed.IsCompleted.Should().BeFalse();

        using var wanted = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        await armed;
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(5), guard.Token);
    }

    [Fact]
    public async Task WaitForTimerAsync_WhenCancelled_IsCancelled()
    {
        var clock = new TestClock();
        using var cancel = new CancellationTokenSource();
        var armed = clock.WaitForTimerAsync(TimeSpan.FromSeconds(5), cancel.Token);

        await cancel.CancelAsync();

        await armed.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WaitForAnyTimerAsync_WhenCancelled_IsCancelled()
    {
        var clock = new TestClock();
        using var cancel = new CancellationTokenSource();
        var armed = clock.WaitForAnyTimerAsync(cancel.Token);

        await cancel.CancelAsync();

        await armed.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WaitForTimerAsync_WithNoTokenAndNoTimer_EndsWithTheHangGuard()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch, TimeSpan.FromMilliseconds(50));

        var wait = () => clock.WaitForTimerAsync(TimeSpan.FromSeconds(5));

        await wait.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task WaitForAnyTimerAsync_WithNoTokenAndNoTimer_EndsWithTheHangGuard()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch, TimeSpan.FromMilliseconds(50));

        var wait = () => clock.WaitForAnyTimerAsync();

        await wait.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task WaitForTimerAsync_ForTheNextPeriodOfATimerThatFires_CompletesWhenItIsArmedAgain()
    {
        var clock = new TestClock();
        using var timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        var rearmed = clock.WaitForTimerAsync(TimeSpan.FromMinutes(2));
        rearmed.IsCompleted.Should().BeFalse("the timer is due in one minute, not two");

        clock.Advance(TimeSpan.FromMinutes(1));

        await rearmed;
    }

    [Fact]
    public void CreateTimer_WithAPeriodicZeroDueTimeThatThrows_LeavesNothingArmed()
    {
        var clock = new TestClock();
        var calls = 0;

        var create = () => clock.CreateTimer(
            _ =>
            {
                calls++;
                throw new InvalidOperationException("first");
            },
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(10));

        create.Should().Throw<InvalidOperationException>();
        clock.PendingTimerCount.Should().Be(0);
        clock.Advance(TimeSpan.FromMinutes(1));
        calls.Should().Be(1, "the timer nobody holds a handle to must not keep firing");
    }
}
