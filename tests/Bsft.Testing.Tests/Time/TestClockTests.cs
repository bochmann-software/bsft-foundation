using Bsft.Testing.Time;
using FluentAssertions;
using Xunit;

namespace Bsft.Testing.Tests.Time;

public class TestClockTests
{
    [Fact]
    public void Default_starts_at_the_unix_epoch()
    {
        var clock = new TestClock();

        clock.GetUtcNow().Should().Be(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Start_instant_is_normalized_to_utc()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.FromHours(2)));

        clock.GetUtcNow().Should().Be(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
        clock.GetUtcNow().Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Advance_moves_the_clock_forward()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch);

        clock.Advance(TimeSpan.FromDays(2));

        clock.GetUtcNow().Should().Be(DateTimeOffset.UnixEpoch.AddDays(2));
    }

    [Fact]
    public void Advance_rejects_moving_backwards()
    {
        var clock = new TestClock();

        var advance = () => clock.Advance(TimeSpan.FromSeconds(-1));

        advance.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("by");
    }

    [Fact]
    public void SetUtcNow_takes_an_absolute_instant_in_either_direction()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch.AddDays(10));

        clock.SetUtcNow(new DateTimeOffset(1969, 7, 20, 20, 17, 0, TimeSpan.Zero));

        clock.GetUtcNow().Should().Be(new DateTimeOffset(1969, 7, 20, 20, 17, 0, TimeSpan.Zero));
    }

    [Fact]
    public void CreateTimer_WhenTheClockIsAdvancedPastItsDueTime_FiresOnceAndOnlyThen()
    {
        var clock = new TestClock();
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromMinutes(4));
        fired.Should().Be(0, "the timer is not due yet");

        clock.Advance(TimeSpan.FromMinutes(1));
        clock.Advance(TimeSpan.FromMinutes(10));

        fired.Should().Be(1, "a one-shot timer fires once");
    }

    [Fact]
    public void CreateTimer_WhenPeriodic_FiresOncePerPeriodPassedByOneAdvance()
    {
        var clock = new TestClock();
        var seenAt = new List<DateTimeOffset>();
        using var timer = clock.CreateTimer(
            _ => seenAt.Add(clock.GetUtcNow()), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        clock.Advance(TimeSpan.FromSeconds(35));

        seenAt.Should().Equal(
            DateTimeOffset.UnixEpoch.AddSeconds(10),
            DateTimeOffset.UnixEpoch.AddSeconds(20),
            DateTimeOffset.UnixEpoch.AddSeconds(30));
        clock.GetUtcNow().Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(35));
    }

    [Fact]
    public void CreateTimer_WithSeveralTimersDue_FiresThemInDueOrder()
    {
        var clock = new TestClock();
        var order = new List<string>();
        using var late = clock.CreateTimer(_ => order.Add("late"), null, TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);
        using var early = clock.CreateTimer(_ => order.Add("early"), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromMinutes(1));

        order.Should().Equal("early", "late");
    }

    [Fact]
    public void CreateTimer_WhenDisposedBeforeItIsDue_NeverFires()
    {
        var clock = new TestClock();
        var fired = 0;
        var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        timer.Dispose();
        clock.Advance(TimeSpan.FromHours(1));

        fired.Should().Be(0);
    }

    [Fact]
    public async Task CreateTimer_WhenDisposedAsyncBeforeItIsDue_NeverFires()
    {
        var clock = new TestClock();
        var fired = 0;
        var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        await ((IAsyncDisposable)timer).DisposeAsync();
        clock.Advance(TimeSpan.FromHours(1));

        fired.Should().Be(0);
    }

    [Fact]
    public void CreateTimer_WhenChangedToInfinite_StopsUntilChangedAgain()
    {
        var clock = new TestClock();
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan).Should().BeTrue();
        clock.Advance(TimeSpan.FromMinutes(1));
        fired.Should().Be(0);

        timer.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        clock.Advance(TimeSpan.FromSeconds(30));

        fired.Should().Be(1);
    }

    [Fact]
    public void CreateTimer_WithAZeroPeriod_FiresOnce()
    {
        var clock = new TestClock();
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), TimeSpan.Zero);

        clock.Advance(TimeSpan.FromMinutes(1));

        fired.Should().Be(1);
    }

    [Fact]
    public void CreateTimer_WhenTheCallbackIsNull_Throws()
    {
        var clock = new TestClock();

        var create = () => clock.CreateTimer(null!, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        create.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateTimer_PassesTheStateToTheCallback()
    {
        var clock = new TestClock();
        object? received = null;
        var state = new object();
        using var timer = clock.CreateTimer(passed => received = passed, state, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(1));

        received.Should().BeSameAs(state);
    }

    [Fact]
    public async Task Delay_OverTheClock_CompletesWhenTheClockPassesItAndNotBefore()
    {
        var clock = new TestClock();
        var delay = Task.Delay(TimeSpan.FromHours(1), clock);

        clock.Advance(TimeSpan.FromMinutes(59));
        delay.IsCompleted.Should().BeFalse();

        clock.Advance(TimeSpan.FromMinutes(1));
        await delay;
    }

    [Fact]
    public void CancellationTokenSource_OverTheClock_CancelsWhenTheClockPassesItsTimeout()
    {
        var clock = new TestClock();
        using var source = new CancellationTokenSource(TimeSpan.FromMinutes(2), clock);

        clock.Advance(TimeSpan.FromMinutes(1));
        source.IsCancellationRequested.Should().BeFalse();

        clock.Advance(TimeSpan.FromMinutes(1));
        source.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void SetUtcNow_WhenMovedForward_FiresTheTimersItPasses()
    {
        var clock = new TestClock();
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromDays(1), Timeout.InfiniteTimeSpan);

        clock.SetUtcNow(DateTimeOffset.UnixEpoch.AddDays(2));

        fired.Should().Be(1);
        clock.GetUtcNow().Should().Be(DateTimeOffset.UnixEpoch.AddDays(2));
    }

    [Fact]
    public void SetUtcNow_WhenMovedBackward_FiresNothing()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch.AddDays(10));
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromDays(1), Timeout.InfiniteTimeSpan);

        clock.SetUtcNow(DateTimeOffset.UnixEpoch);

        fired.Should().Be(0);
        clock.UtcNow.Should().Be(DateTimeOffset.UnixEpoch);
    }
}
