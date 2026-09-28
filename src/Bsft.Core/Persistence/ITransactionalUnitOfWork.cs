namespace Bsft.Core.Persistence;

/// <summary>
/// Runs several writes as one unit, so that either all of them are durable or none of them are.
/// </summary>
/// <remarks>
/// <para>
/// The companion of <see cref="IUnitOfWork"/> and deliberately not derived from it. Committing what a
/// scope staged and fencing a piece of work are two capabilities, and the packages that need them are
/// not the same packages: a notification sweep only ever commits, while applying a payment provider's
/// webhook has to claim the delivery and move the subscription together or a crash between the two
/// loses the change for good. Keeping them apart also keeps the migration of the private seams they
/// replace source-compatible, because neither contract adds a member to the interfaces that adopt it.
/// An implementation that can do both implements both.
/// </para>
/// <para>
/// The unit is expressed as a scope the caller hands over rather than as a transaction handle it
/// opens, because a handle cannot be retried. A host that configured retries — which is what EF Core's
/// <c>EnableRetryOnFailure</c> is for, and what a managed SQL Server or PostgreSQL deployment needs —
/// re-runs the whole unit from the start after a transient failure, and only an implementation that
/// owns the delegate can do that. It is also what lets an in-memory implementation be honest: a
/// process-local dictionary has nothing to roll back to, so it runs the work directly, which a handle
/// with <c>Commit</c> and <c>Rollback</c> on it could only pretend to do.
/// </para>
/// </remarks>
public interface ITransactionalUnitOfWork
{
    /// <summary>Runs the work as one unit and answers what it produced.</summary>
    /// <typeparam name="TResult">What the work answers.</typeparam>
    /// <param name="work">
    /// The writes to make durable together. It may be run more than once: an implementation that
    /// retries a transient failure re-runs the whole unit from the start, having discarded everything
    /// the failed attempt wrote.
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>What <paramref name="work"/> answered, once its writes are durable.</returns>
    /// <remarks>
    /// An exception out of <paramref name="work"/> discards the unit's writes and is then rethrown, so
    /// a caller sees the failure and the storage never sees half of the change. The context the writes
    /// were made through is not usable afterwards — its change tracker still describes rows that were
    /// rolled back — which is why a unit is expected to span a request, a delivery or a job run, all of
    /// which are abandoned when they fail.
    /// </remarks>
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default);
}
