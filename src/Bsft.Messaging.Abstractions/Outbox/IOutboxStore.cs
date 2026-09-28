namespace Bsft.Messaging.Outbox;

/// <summary>
/// Storage contract for the transactional outbox. The store hands out leases on pending messages and
/// records their outcome; it never publishes anything itself.
/// </summary>
/// <remarks>
/// The contract is written for at-least-once delivery. A message is marked processed only after it
/// has been published, so a crash between the two leaves the message pending and it is delivered
/// again once its lease expires. Losing a message is not possible; receiving one twice is. A message
/// that keeps failing is retired rather than deleted, so it stays available for inspection.
/// </remarks>
public interface IOutboxStore
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> pending messages whose lease is absent or expired and
    /// that are not being held back for a later attempt, and leases them until <paramref name="now"/>
    /// plus <paramref name="leaseDuration"/>.
    /// </summary>
    /// <param name="now">The current UTC instant.</param>
    /// <param name="leaseDuration">How long the claim holds before another instance may take over.</param>
    /// <param name="batchSize">The maximum number of messages to claim.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The messages claimed by this call; empty when nothing was pending.</returns>
    Task<IReadOnlyList<LeasedOutboxMessage>> LeaseAsync(
        DateTimeOffset now, TimeSpan leaseDuration, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a leased message processed so it is never delivered again.
    /// </summary>
    /// <param name="id">The row identity of the message.</param>
    /// <param name="leaseToken">The token proving the caller holds the lease.</param>
    /// <param name="processedAt">The UTC instant at which the message was published.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// <see langword="true"/> when the message was marked; <see langword="false"/> when the lease had
    /// already expired and another instance took the message over.
    /// </returns>
    Task<bool> TryMarkProcessedAsync(long id, Guid leaseToken, DateTimeOffset processedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives a leased message back after a failed delivery: the lease is released, the attempt is
    /// counted, and the message becomes eligible for another dispatcher at
    /// <paramref name="nextAttemptAt"/>.
    /// </summary>
    /// <param name="id">The row identity of the message.</param>
    /// <param name="leaseToken">The token proving the caller holds the lease.</param>
    /// <param name="nextAttemptAt">
    /// The earliest UTC instant at which <see cref="LeaseAsync"/> may hand the message out again. It is
    /// recorded with the row rather than kept by the dispatcher, so the wait survives a restart and is
    /// respected by every instance rather than by the one that failed. An instant in the past is not an
    /// error: it means the message is eligible again at once, which is what a caller that wants no
    /// backoff passes.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the lease has been released.</returns>
    Task AbandonAsync(long id, Guid leaseToken, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a message that has failed too often, recording why. The row is kept and stays
    /// inspectable, but it is no longer leased, so one message that can never be delivered — a payload
    /// whose type was retired, a consumer that always throws — cannot occupy the dispatcher forever.
    /// </summary>
    /// <param name="id">The row identity of the message.</param>
    /// <param name="leaseToken">The token proving the caller holds the lease.</param>
    /// <param name="deadLetteredAt">The UTC instant at which the message was retired.</param>
    /// <param name="reason">
    /// The failure that retired the message. It is stored with the row, and an exception raised while
    /// deserializing or handling an event can quote parts of that event, so a store implementation has
    /// to treat it as at least as sensitive as the payload itself.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the message has been retired.</returns>
    Task DeadLetterAsync(
        long id, Guid leaseToken, DateTimeOffset deadLetteredAt, string reason, CancellationToken cancellationToken = default);
}
