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
}
