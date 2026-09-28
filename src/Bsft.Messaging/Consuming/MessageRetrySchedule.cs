using Bsft.Outbox;

namespace Bsft.Messaging.Consuming;

/// <summary>
/// The default <see cref="IMessageRetrySchedule"/>: exponential from the caller's base, capped at the
/// caller's ceiling, with part of each interval randomised away, and with a failure's own
/// <see cref="Core.Retry.IRetryAfter"/> honoured as a floor.
/// </summary>
/// <remarks>
/// <para>
/// The jitter is subtracted from the computed interval rather than added to it. Adding would let the
/// last step of the schedule exceed the ceiling the application configured, which is the one number an
/// operator reasons about when they ask how long a message may be held back.
/// </para>
/// <para>
/// The randomness is an injected <see cref="Random"/> rather than <see cref="Random.Shared"/> reached
/// for inside the method, so that a test can assert the whole schedule rather than a range. Nothing
/// here is a security decision, so a seeded generator is entirely adequate.
/// </para>
/// <para>
/// The arithmetic itself lives in <c>Bsft.Outbox</c>, because the outbox is not the only thing
/// that needs a growing wait: this type answers the messaging seam and forwards. Two copies of an
/// exponential schedule is how the jitter and the ceiling drift apart.
/// </para>
/// </remarks>
internal sealed class MessageRetrySchedule : IMessageRetrySchedule
{
    private readonly IOutboxRetrySchedule _schedule;

    /// <summary>Initializes a new instance of the <see cref="MessageRetrySchedule"/> class.</summary>
    /// <param name="schedule">The schedule the arithmetic actually lives in.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="schedule"/> is null.</exception>
    public MessageRetrySchedule(IOutboxRetrySchedule schedule)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
    }

    /// <inheritdoc />
    public TimeSpan NextDelay(int attemptNumber, TimeSpan backoffBase, TimeSpan maxDelay, TimeSpan? retryAfter)
        => _schedule.NextDelay(attemptNumber, backoffBase, maxDelay, retryAfter);

    /// <inheritdoc />
    public TimeSpan? RequestedRetryAfter(Exception failure) => _schedule.RequestedRetryAfter(failure);
}
