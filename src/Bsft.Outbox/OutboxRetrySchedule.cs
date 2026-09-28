using Bsft.Core.Retry;

namespace Bsft.Outbox;

/// <summary>
/// The default <see cref="IOutboxRetrySchedule"/>: exponential from the caller's base, capped at the
/// caller's ceiling, with part of each interval randomised away, and with a failure's own
/// <see cref="IRetryAfter"/> honoured as a floor.
/// </summary>
/// <remarks>
/// <para>
/// The jitter is subtracted from the computed interval rather than added to it. Adding would let the
/// last step of the schedule exceed the ceiling the application configured, which is the one number
/// an operator reasons about when they ask how long a row may be held back.
/// </para>
/// <para>
/// The randomness is an injected <see cref="Random"/> rather than <see cref="Random.Shared"/> reached
/// for inside the method, so that a test can assert the whole schedule rather than a range. Nothing
/// here is a security decision, so a seeded generator is entirely adequate.
/// </para>
/// </remarks>
public sealed class OutboxRetrySchedule : IOutboxRetrySchedule
{
    /// <summary>
    /// The largest exponent that is worth computing. Doubling stops mattering once the result is past
    /// the ceiling, and it keeps a row whose attempt count has been reset by hand a few times from
    /// reaching an exponent that overflows a double.
    /// </summary>
    private const int MaxExponent = 30;

    /// <summary>
    /// The fraction of each interval that is randomised away, spreading the retries of rows that
    /// failed together instead of letting them fall on the same instant for the rest of their lives.
    /// </summary>
    /// <remarks>
    /// A constant rather than an option, because it is the one number in the schedule an operator has
    /// no way to reason about: the base and the ceiling answer questions somebody asks ("how soon",
    /// "how long at most"), while the jitter fraction only trades collision probability against how
    /// closely the schedule matches its own description.
    /// </remarks>
    private const double JitterFraction = 0.2;

    /// <summary>
    /// How deep the search for a requested wait follows wrapped failures. It bounds a chain that only
    /// a bug could make long, and nothing legitimate nests an exception thirty-two deep.
    /// </summary>
    private const int MaxFailureDepth = 32;

    private readonly Random _random;

    /// <summary>Initializes a new instance of the <see cref="OutboxRetrySchedule"/> class.</summary>
    /// <param name="random">The source of the jitter.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is null.</exception>
    public OutboxRetrySchedule(Random random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    /// <inheritdoc />
    public TimeSpan NextDelay(int attemptNumber, TimeSpan backoffBase, TimeSpan maxDelay, TimeSpan? retryAfter)
    {
        var interval = Jittered(Exponential(attemptNumber, backoffBase, maxDelay));

        if (retryAfter is not { } asked || asked <= TimeSpan.Zero)
            return interval;

        // A floor and never a ceiling: a service may ask for more time than the schedule would have
        // given it, and the clamp is what stops it asking for a week.
        var clamped = asked > maxDelay ? maxDelay : asked;

        return clamped > interval ? clamped : interval;
    }

    /// <inheritdoc />
    public TimeSpan? RequestedRetryAfter(Exception failure)
    {
        if (failure is null)
            throw new ArgumentNullException(nameof(failure));

        return LongestAsk(failure, MaxFailureDepth);
    }

    /// <summary>Searches a failure and everything it wraps for the longest wait asked for.</summary>
    private static TimeSpan? LongestAsk(Exception failure, int remainingDepth)
    {
        if (remainingDepth <= 0)
            return null;

        var longest = failure is IRetryAfter { RetryAfter: { } asked } && asked > TimeSpan.Zero
            ? asked
            : (TimeSpan?)null;

        // An AggregateException's InnerException is one of its InnerExceptions, so the branches are
        // exclusive rather than cumulative: walking both would visit the same failure twice.
        if (failure is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                longest = Longer(longest, LongestAsk(inner, remainingDepth - 1));
        }
        else if (failure.InnerException is { } wrapped)
        {
            longest = Longer(longest, LongestAsk(wrapped, remainingDepth - 1));
        }

        return longest;
    }

    /// <summary>Picks the longer of two asks, either of which may be absent.</summary>
    private static TimeSpan? Longer(TimeSpan? left, TimeSpan? right)
    {
        if (left is not { } first)
            return right;

        return right is { } second && second > first ? second : first;
    }

    /// <summary>Doubles the base once per attempt already made, stopping at the ceiling.</summary>
    private static TimeSpan Exponential(int attemptNumber, TimeSpan backoffBase, TimeSpan maxDelay)
    {
        if (backoffBase <= TimeSpan.Zero || maxDelay <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var exponent = Math.Clamp(attemptNumber - 1, 0, MaxExponent);
        var ticks = backoffBase.Ticks * Math.Pow(2, exponent);

        return ticks >= maxDelay.Ticks ? maxDelay : TimeSpan.FromTicks((long)ticks);
    }

    /// <summary>Removes a random slice of the interval, up to <see cref="JitterFraction"/> of it.</summary>
    private TimeSpan Jittered(TimeSpan interval) =>
        interval <= TimeSpan.Zero
            ? interval
            : TimeSpan.FromTicks((long)(interval.Ticks * (1 - (JitterFraction * _random.NextDouble()))));
}
