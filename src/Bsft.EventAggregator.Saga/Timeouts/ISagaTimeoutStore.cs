namespace Bsft.EventAggregator.Saga.Timeouts;

/// <summary>
/// Persistence contract for saga timeouts.
/// </summary>
/// <remarks>
/// <see cref="TryDispatchAsync"/> deliberately owns the whole "confirm, publish, claim" sequence
/// instead of exposing the three separately: an implementation has to be free to choose how it makes
/// the claim atomic. Publishing happens between the confirmation and the claim, so delivery is
/// at-least-once — a dispatch that succeeds but whose claim loses the race is published twice, and
/// the handling of a timeout event must therefore be idempotent.
/// </remarks>
public interface ISagaTimeoutStore
{
    /// <summary>Persists a new timeout, replacing any pending timeout of the same name.</summary>
    /// <param name="timeout">The timeout to store.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task ScheduleAsync(SagaTimeout timeout, CancellationToken cancellationToken = default);

    /// <summary>Removes a single pending timeout by name.</summary>
    /// <param name="sagaType">The saga type.</param>
    /// <param name="correlationKey">The saga correlation key.</param>
    /// <param name="name">The timeout name.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task CancelAsync(string sagaType, string correlationKey, string name, CancellationToken cancellationToken = default);

    /// <summary>Removes all pending timeouts scheduled by a given step.</summary>
    /// <param name="sagaType">The saga type.</param>
    /// <param name="correlationKey">The saga correlation key.</param>
    /// <param name="stepIndex">The logical step index whose timeouts are dropped.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task CancelForStepAsync(string sagaType, string correlationKey, int stepIndex, CancellationToken cancellationToken = default);

    /// <summary>Removes every pending timeout of a saga instance.</summary>
    /// <param name="sagaType">The saga type.</param>
    /// <param name="correlationKey">The saga correlation key.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task CancelAllAsync(string sagaType, string correlationKey, CancellationToken cancellationToken = default);

    /// <summary>Reads the pending timeouts that are due at or before the given instant.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="batchSize">The maximum number of timeouts to return.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The due timeouts, oldest first.</returns>
    Task<IReadOnlyList<SagaTimeout>> ListDueAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms the timeout is still pending, runs <paramref name="dispatch"/>, then claims it. A
    /// timeout that was cancelled or already claimed is never published.
    /// </summary>
    /// <param name="timeout">The timeout as read by <see cref="ListDueAsync"/>, carrying the token the claim is checked against.</param>
    /// <param name="dispatchedAt">The instant to record as the dispatch time.</param>
    /// <param name="dispatch">The callback that publishes the timeout event.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// <c>true</c> when this caller dispatched the timeout; <c>false</c> when it was already gone or
    /// another instance won the race.
    /// </returns>
    Task<bool> TryDispatchAsync(
        SagaTimeout timeout,
        DateTimeOffset dispatchedAt,
        Func<SagaTimeout, CancellationToken, Task> dispatch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks timeouts as skipped without publishing their events. Takes a batch because a catch-up
    /// policy can discard a whole poll batch at once, and one statement beats a hundred round-trips.
    /// </summary>
    /// <param name="ids">The identities of the timeouts to skip.</param>
    /// <param name="skippedAt">The instant to record.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task SkipAsync(IReadOnlyCollection<long> ids, DateTimeOffset skippedAt, CancellationToken cancellationToken = default);

    /// <summary>Increments the failed attempt counter of a timeout so a poison row is visible.</summary>
    /// <param name="id">The identity of the timeout.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task RecordFailedAttemptAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a timeout that has failed too often, recording why and counting the attempt that
    /// retired it. The row is kept and stays readable, but it leaves the due set for good, so one
    /// timer that can never fire cannot hold a place in every poll batch and stop the timers of the
    /// whole application.
    /// </summary>
    /// <param name="id">The identity of the timeout.</param>
    /// <param name="deadLetteredAt">The instant to record as the retirement time.</param>
    /// <param name="reason">
    /// The failure that retired the timeout. It is stored with the row, and an exception raised while
    /// resolving or publishing a payload can quote parts of that payload, so an implementation has to
    /// treat it as at least as sensitive as the payload itself.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <remarks>
    /// Only a still-pending timeout may be retired. A timeout that was dispatched or cancelled in the
    /// meantime is left exactly as it is, so a retirement racing a successful dispatch cannot rewrite
    /// a fired timer into a failed one.
    /// </remarks>
    Task DeadLetterAsync(long id, DateTimeOffset deadLetteredAt, string reason, CancellationToken cancellationToken = default);
}
