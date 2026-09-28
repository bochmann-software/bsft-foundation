using Bsft.Messaging.Consuming;
using Bsft.Outbox;

namespace Bsft.Messaging.Outbox;

/// <summary>
/// Presents this package's retry seam as the one the drain kernel asks for.
/// </summary>
/// <remarks>
/// <see cref="IMessageRetrySchedule"/> is public and is what a host replaces to change how long a
/// failed message waits. The kernel asks the same question through a seam of its own, so the two are
/// bridged here rather than the host being asked to implement both.
/// </remarks>
internal sealed class MessagingOutboxRetrySchedule : IOutboxRetrySchedule
{
    private readonly IMessageRetrySchedule _schedule;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagingOutboxRetrySchedule"/> class.
    /// </summary>
    /// <param name="schedule">The schedule this package publishes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="schedule"/> is null.</exception>
    public MessagingOutboxRetrySchedule(IMessageRetrySchedule schedule)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
    }

    /// <inheritdoc />
    public TimeSpan NextDelay(int attemptNumber, TimeSpan backoffBase, TimeSpan maxDelay, TimeSpan? retryAfter)
        => _schedule.NextDelay(attemptNumber, backoffBase, maxDelay, retryAfter);

    /// <inheritdoc />
    public TimeSpan? RequestedRetryAfter(Exception failure) => _schedule.RequestedRetryAfter(failure);
}
