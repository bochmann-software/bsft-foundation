namespace Bsft.Outbox;

/// <summary>
/// Decides how long a failed outbox row waits before it is attempted again: an exponential schedule
/// the caller supplies the bounds of, and a wait the failure itself asked for.
/// </summary>
/// <remarks>
/// Nothing here sleeps. The schedule produces a duration and the drainer records it on the row, so
/// the wait costs a row that is invisible until an instant rather than a thread that is held.
/// </remarks>
public interface IOutboxRetrySchedule
{
    /// <summary>
    /// Computes how long to hold a row back after its <paramref name="attemptNumber"/>-th attempt
    /// failed.
    /// </summary>
    /// <param name="attemptNumber">
    /// The one-based number of the attempt that just failed. The schedule doubles once per attempt
    /// already made, so the first failure waits <paramref name="backoffBase"/>.
    /// </param>
    /// <param name="backoffBase">
    /// The first interval of the schedule. <see cref="TimeSpan.Zero"/> disables the exponential part
    /// entirely, which leaves a failure's own request as the only thing that can produce a wait.
    /// </param>
    /// <param name="maxDelay">
    /// The ceiling. It caps the exponential schedule and clamps <paramref name="retryAfter"/>, so it
    /// is the one number that answers "how long can a single row be held back".
    /// </param>
    /// <param name="retryAfter">
    /// What the failure asked for, or <see langword="null"/> when it asked for nothing. It acts as a
    /// floor: a request longer than the computed interval wins, a shorter one does not shorten the
    /// schedule, and neither may exceed <paramref name="maxDelay"/>.
    /// </param>
    /// <returns>How long to wait; never negative and never above <paramref name="maxDelay"/>.</returns>
    TimeSpan NextDelay(int attemptNumber, TimeSpan backoffBase, TimeSpan maxDelay, TimeSpan? retryAfter);

    /// <summary>Reads the wait a failure asked for, by looking through it and the failures it wraps.</summary>
    /// <param name="failure">The failure that ended the attempt.</param>
    /// <returns>
    /// The longest positive wait asked for anywhere in the failure, or <see langword="null"/> when
    /// nothing in it asked for one.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure"/> is null.</exception>
    TimeSpan? RequestedRetryAfter(Exception failure);
}
