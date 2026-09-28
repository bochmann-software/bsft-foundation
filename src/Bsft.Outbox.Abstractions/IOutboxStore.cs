namespace Bsft.Outbox;

/// <summary>
/// Storage contract for a transactional outbox. The store hands out leases on pending rows and
/// records their outcome; it never applies a side effect itself.
/// </summary>
/// <typeparam name="TPayload">What the rows of this outbox carry.</typeparam>
/// <remarks>
/// The contract is written for at-least-once application. A row is marked processed only after its
/// side effect has been applied, so a crash between the two leaves the row pending and the effect is
/// applied again once the lease expires. Losing a row is not possible; applying one twice is, which
/// is why a side effect has to be idempotent. A row that keeps failing is retired rather than
/// deleted, so it stays available for inspection.
/// </remarks>
public interface IOutboxStore<TPayload>
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> pending rows whose lease is absent or expired and
    /// that are not being held back for a later attempt, and leases them until <paramref name="now"/>
    /// plus <paramref name="leaseDuration"/>.
    /// </summary>
    /// <param name="now">The current UTC instant.</param>
    /// <param name="leaseDuration">How long the claim holds before another instance may take over.</param>
    /// <param name="batchSize">The maximum number of rows to claim.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The rows claimed by this call; empty when nothing was pending.</returns>
    Task<IReadOnlyList<LeasedOutboxRow<TPayload>>> LeaseAsync(
        DateTimeOffset now, TimeSpan leaseDuration, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>Marks a leased row processed so its side effect is never applied again.</summary>
    /// <param name="id">The row identity.</param>
    /// <param name="leaseToken">The token proving the caller holds the lease.</param>
    /// <param name="processedAt">The UTC instant at which the side effect was applied.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// <see langword="true"/> when the row was marked; <see langword="false"/> when the lease had
    /// already expired and another instance took the row over.
    /// </returns>
    Task<bool> TryMarkProcessedAsync(
        long id, Guid leaseToken, DateTimeOffset processedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives a leased row back after a failed attempt: the lease is released, the attempt is counted,
    /// and the row becomes eligible again at <paramref name="nextAttemptAt"/>.
    /// </summary>
    /// <param name="id">The row identity.</param>
    /// <param name="leaseToken">The token proving the caller holds the lease.</param>
    /// <param name="nextAttemptAt">
    /// The earliest UTC instant at which <see cref="LeaseAsync"/> may hand the row out again. It is
    /// recorded with the row rather than kept by the drainer, so the wait survives a restart and is
    /// respected by every instance rather than by the one that failed. An instant in the past is not
    /// an error: it means the row is eligible at once, which is what a caller that wants no backoff
    /// passes.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the lease has been released.</returns>
    /// <remarks>
    /// An implementation must leave the row untouched when <paramref name="leaseToken"/> is not the
    /// one the row currently carries. A caller whose lease expired mid-attempt is no longer the
    /// holder, and releasing a lease another instance has since taken would hand the same row to two
    /// drainers at once. There is no return value for it because the caller has nothing left to do
    /// either way: the row belongs to somebody else now.
    /// </remarks>
    Task AbandonAsync(long id, Guid leaseToken, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a row that has failed too often, recording why. The row is kept and stays inspectable,
    /// but it is no longer leased, so one row whose side effect can never succeed cannot occupy the
    /// drainer forever.
    /// </summary>
    /// <param name="id">The row identity.</param>
    /// <param name="leaseToken">The token proving the caller holds the lease.</param>
    /// <param name="deadLetteredAt">The UTC instant at which the row was retired.</param>
    /// <param name="reason">
    /// The failure that retired the row. It is stored with the row, and an exception raised while
    /// applying a side effect can quote parts of the payload, so a store implementation has to treat
    /// it as at least as sensitive as the payload itself.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the row has been retired.</returns>
    /// <remarks>
    /// As with <see cref="AbandonAsync"/>, an implementation must leave the row untouched when
    /// <paramref name="leaseToken"/> is not the one the row currently carries. Retiring a row the
    /// caller no longer holds would take it away from the instance that does, on the strength of a
    /// failure that instance never saw.
    /// </remarks>
    Task DeadLetterAsync(
        long id, Guid leaseToken, DateTimeOffset deadLetteredAt, string reason, CancellationToken cancellationToken = default);
}
