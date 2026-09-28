using Bsft.Core.Retry;
using FluentAssertions;
using Xunit;

namespace Bsft.Outbox.Tests;

public class OutboxRetryScheduleTests
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(5);

    /// <summary>A generator that never randomises anything away, so an interval is its plain value.</summary>
    private static OutboxRetrySchedule WithoutJitter() => new(new ZeroRandom());

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(4, 80)]
    public void The_interval_doubles_once_per_attempt_already_made(int attemptNumber, int expectedSeconds)
    {
        WithoutJitter()
            .NextDelay(attemptNumber, Base, Ceiling, retryAfter: null)
            .Should()
            .Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void The_doubling_stops_at_the_ceiling()
    {
        WithoutJitter().NextDelay(20, Base, Ceiling, retryAfter: null).Should().Be(Ceiling);
    }

    [Fact]
    public void An_attempt_count_far_past_the_ceiling_does_not_overflow_the_arithmetic()
    {
        // A row whose attempt count was reset by hand a few times must not reach an exponent that
        // overflows; the clamp on the exponent is what keeps the answer the ceiling instead of chaos.
        WithoutJitter().NextDelay(int.MaxValue, Base, Ceiling, retryAfter: null).Should().Be(Ceiling);
    }

    [Fact]
    public void A_base_of_zero_produces_no_wait_at_all()
    {
        // The consumer's default: it behaves exactly as it did before a schedule existed.
        WithoutJitter().NextDelay(5, TimeSpan.Zero, Ceiling, retryAfter: null).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void A_ceiling_of_zero_leaves_nothing_to_wait_for()
    {
        WithoutJitter().NextDelay(3, Base, TimeSpan.Zero, TimeSpan.FromMinutes(1)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void The_jitter_is_subtracted_so_the_ceiling_is_never_exceeded()
    {
        var schedule = new OutboxRetrySchedule((new MaximumRandom()));

        var delay = schedule.NextDelay(1, Base, Ceiling, retryAfter: null);

        delay.Should().BeLessThan(Base).And.Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void A_wait_that_was_asked_for_is_a_floor_and_not_a_replacement()
    {
        var schedule = WithoutJitter();

        schedule.NextDelay(1, Base, Ceiling, TimeSpan.FromMinutes(2)).Should().Be(TimeSpan.FromMinutes(2));
        schedule.NextDelay(1, Base, Ceiling, TimeSpan.FromSeconds(1)).Should().Be(Base);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_wait_of_zero_or_less_asks_for_nothing(int askedSeconds)
    {
        WithoutJitter()
            .NextDelay(1, Base, Ceiling, TimeSpan.FromSeconds(askedSeconds))
            .Should()
            .Be(Base);
    }

    [Fact]
    public void A_wait_longer_than_the_ceiling_is_clamped_to_it()
    {
        // What stops a service asking to be left alone for a week from removing a message for a week.
        WithoutJitter().NextDelay(1, Base, Ceiling, TimeSpan.FromDays(7)).Should().Be(Ceiling);
    }

    [Fact]
    public void A_failure_that_asks_for_nothing_reads_as_no_request()
    {
        WithoutJitter().RequestedRetryAfter(new InvalidOperationException("transport down")).Should().BeNull();
    }

    [Fact]
    public void A_failure_that_asks_directly_is_read()
    {
        WithoutJitter()
            .RequestedRetryAfter(new RetryAfterException("throttled", TimeSpan.FromSeconds(30)))
            .Should()
            .Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void An_ask_wrapped_in_an_aggregate_is_still_read()
    {
        // Which is how it always arrives: the pipeline observes a faulted task rather than catching.
        var failure = new AggregateException(new RetryAfterException("throttled", TimeSpan.FromSeconds(45)));

        WithoutJitter().RequestedRetryAfter(failure).Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void An_ask_wrapped_by_the_layer_above_it_is_still_read()
    {
        var failure = new InvalidOperationException(
            "publishing failed", new RetryAfterException("throttled", TimeSpan.FromSeconds(15)));

        WithoutJitter().RequestedRetryAfter(failure).Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Of_several_asks_the_longest_is_the_one_that_satisfies_all_of_them()
    {
        var failure = new AggregateException(
            new RetryAfterException("one handler", TimeSpan.FromSeconds(20)),
            new RetryAfterException("another handler", TimeSpan.FromSeconds(70)));

        WithoutJitter().RequestedRetryAfter(failure).Should().Be(TimeSpan.FromSeconds(70));
    }

    [Fact]
    public void An_ask_of_zero_is_no_ask()
    {
        WithoutJitter().RequestedRetryAfter(new RetryAfterException("throttled", TimeSpan.Zero)).Should().BeNull();
    }

    [Fact]
    public void A_failure_nested_deeper_than_the_search_goes_is_given_up_on_rather_than_followed_forever()
    {
        Exception failure = new RetryAfterException("throttled", TimeSpan.FromSeconds(30));

        for (var depth = 0; depth < 40; depth++)
            failure = new InvalidOperationException("wrapped", failure);

        WithoutJitter().RequestedRetryAfter(failure).Should().BeNull();
    }

    [Fact]
    public void A_missing_failure_is_rejected()
    {
        var act = () => WithoutJitter().RequestedRetryAfter(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("failure");
    }

    [Fact]
    public void A_missing_random_is_rejected()
    {
        var act = () => new OutboxRetrySchedule(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("random");
    }

    /// <summary>Randomness that always answers zero, which subtracts nothing.</summary>
    private sealed class ZeroRandom : Random
    {
        public override double NextDouble() => 0;
    }

    /// <summary>Randomness that always answers one, which subtracts the whole jitter fraction.</summary>
    private sealed class MaximumRandom : Random
    {
        public override double NextDouble() => 1;
    }
}
