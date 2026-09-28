
namespace Bsft.Messaging.Consuming;

/// <summary>
/// Decides how long a failed message waits before it is attempted again: an exponential schedule the
/// caller supplies the bounds of, and a wait the failure itself asked for.
/// </summary>
/// <remarks>
/// <para>
/// Both halves of the pipeline ask the same question and get it answered here rather than each
/// answering it themselves: <see cref="Outbox.OutboxDispatcher"/> before it hands a row back to the store, and
/// <see cref="IntegrationEventConsumer"/> before it hands a delivery back to the transport. They differ
/// only in the bounds they pass, because a wait costs the two sides differently — a row that is invisible
/// until an instant holds nothing, a transport that cannot schedule may hold a consumer.
/// </para>
/// <para>
/// Nothing here sleeps. The schedule produces a duration and the caller records or forwards it; the two
/// components that own retrying are the only ones entitled to decide what a wait costs.
/// </para>
/// </remarks>
public interface IMessageRetrySchedule
{
    /// <summary>
    /// Computes how long to hold a message back after its <paramref name="attemptNumber"/>-th attempt
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
    /// The ceiling. It caps the exponential schedule and clamps <paramref name="retryAfter"/>, so it is
    /// the one number that answers "how long can a single message be held back".
    /// </param>
    /// <param name="retryAfter">
    /// What the failure asked for, or <see langword="null"/> when it asked for nothing. It acts as a
    /// floor: a request longer than the computed interval wins, a shorter one does not shorten the
    /// schedule, and neither may exceed <paramref name="maxDelay"/>.
    /// </param>
    /// <returns>How long to wait; never negative and never above <paramref name="maxDelay"/>.</returns>
    TimeSpan NextDelay(int attemptNumber, TimeSpan backoffBase, TimeSpan maxDelay, TimeSpan? retryAfter);

    /// <summary>
    /// Reads the wait a failure asked for, by looking for <see cref="Core.Retry.IRetryAfter"/> in it and
    /// in the failures it wraps.
    /// </summary>
    /// <param name="failure">The failure that ended the attempt.</param>
    /// <returns>
    /// The longest positive wait asked for anywhere in the failure, or <see langword="null"/> when
    /// nothing in it asked for one.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure"/> is null.</exception>
    /// <remarks>
    /// It searches rather than tests the outermost type because of where the ask is raised: a handler's
    /// exception reaches the pipeline wrapped in an <see cref="AggregateException"/>, and a channel's
    /// throttling exception is routinely rethrown by the layer above it. The longest ask wins where
    /// several were made, since it is the only one that satisfies all of them.
    /// </remarks>
    TimeSpan? RequestedRetryAfter(Exception failure);
}
