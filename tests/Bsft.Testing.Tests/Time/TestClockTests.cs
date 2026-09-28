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
}
