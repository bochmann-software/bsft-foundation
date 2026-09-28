namespace Bsft.Idempotency;

/// <summary>
/// Where idempotency records live. It is an interface rather than a cache because the guarantee a
/// host wants depends on where the record is written: a store that can enlist in the host's own
/// transaction makes the reservation and the business row one atomic outcome, and a cache never can.
/// </summary>
/// <remarks>
/// <para>
/// The contract has one hard requirement, and it is not "look, then write". Two concurrent requests
/// under one key both find nothing, so a read followed by an insert lets both proceed. The insert
/// itself must be the arbiter — a unique index on <c>(Scope, Key)</c> in a relational store, an
/// atomic add in an in-memory one — and the loser of that race must report
/// <see cref="IdempotencyBeginOutcome.InProgress"/> rather than fail.
/// </para>
/// <para>
/// The default implementation is in-process and says so: two replicas have two memories, and a
/// duplicate that lands on the other one runs. <c>Bsft.Idempotency.EFCore</c> is the shared store.
/// </para>
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Claims a key for a request, or reports why it cannot be claimed.
    /// </summary>
    /// <param name="request">Which caller, which key, which request, and how long the lease stands.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>What happened, and the stored response or lease the caller needs to answer with.</returns>
    Task<IdempotencyBeginResult> BeginAsync(
        IdempotencyReservationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the response of a request that finished, turning its reservation into a replayable
    /// record.
    /// </summary>
    /// <param name="request">The reservation to complete and what to store against it.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the record has been written.</returns>
    Task CompleteAsync(IdempotencyCompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a reservation so the key is immediately free again.
    /// </summary>
    /// <param name="scope">The caller the key belongs to.</param>
    /// <param name="key">The key to release.</param>
    /// <param name="fingerprint">The digest the key was reserved with, so only its own row is dropped.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the reservation is gone.</returns>
    /// <remarks>
    /// This is what a server error and a non-replayable status do, and it is deliberate: the client
    /// asked to be allowed to retry, and a record that outlived a failure would refuse exactly that.
    /// </remarks>
    Task ReleaseAsync(string scope, string key, string fingerprint, CancellationToken cancellationToken = default);
}
