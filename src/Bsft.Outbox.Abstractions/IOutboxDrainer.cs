namespace Bsft.Outbox;

/// <summary>
/// Runs a single drain pass over one outbox on demand, instead of waiting for a polling loop to come
/// round.
/// </summary>
/// <typeparam name="TPayload">What the rows of this outbox carry.</typeparam>
/// <remarks>
/// This is the supported seam for a test that has to observe the effect of a staged row: stage the
/// change, commit it, drain, and assert. It is the same instance a host runs its loop over, so
/// draining exercises the real lease, retry and retirement policy rather than a reimplementation
/// that can drift.
/// </remarks>
public interface IOutboxDrainer<TPayload>
{
    /// <summary>Leases one batch of pending rows and applies their side effect.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// The number of rows applied and marked processed in this pass. A pass drains at most one batch,
    /// so a caller draining a backlog repeats until the count is zero.
    /// </returns>
    Task<int> DrainOnceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Leases one batch of pending rows and applies their side effect, starting a row only while the host
    /// allows it.
    /// </summary>
    /// <param name="mayStartRow">
    /// Asked before the batch is leased and before each row, never inside a row: it answers
    /// <see langword="false"/> once no further row may be started, a host stopping for example. The row
    /// in flight runs to its processed mark on <paramref name="cancellationToken"/>. Rows leased but not
    /// started stay pending under their lease and are claimed again once it expires.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to observe for cancellation of the row in flight and of the store calls, which a hosted
    /// worker cancels only a grace period after <paramref name="mayStartRow"/> turned <see langword="false"/>.
    /// </param>
    /// <returns>The number of rows applied and marked processed in this pass.</returns>
    /// <remarks>
    /// The default implementation ignores <paramref name="mayStartRow"/> and drains as
    /// <see cref="DrainOnceAsync(CancellationToken)"/> does, so an implementation written before this
    /// overload keeps working; it just cannot stop between rows.
    /// </remarks>
    Task<int> DrainOnceAsync(Func<bool> mayStartRow, CancellationToken cancellationToken = default) =>
        DrainOnceAsync(cancellationToken);
}
