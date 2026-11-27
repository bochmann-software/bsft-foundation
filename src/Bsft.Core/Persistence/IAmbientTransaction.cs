namespace Bsft.Core.Persistence;

/// <summary>
/// A transaction that spans every store enlisted in the current scope, which a unit started while it
/// is open joins instead of opening one of its own.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam a modular host needs once one request writes through several stores that share
/// one database connection: a command that dispatches commands to other modules, each with its own
/// context, wants all of their writes to commit or roll back together. The outermost
/// <see cref="ITransactionalUnitOfWork.ExecuteAsync{TResult}"/> opens the transaction and commits it
/// once, after the work returned; every unit started inside it — a nested command, a package typed
/// against <see cref="ITransactionalUnitOfWork"/> — runs in that same transaction and commits nothing
/// on its own. A failure anywhere rolls back everything, including what an earlier nested unit wrote.
/// </para>
/// <para>
/// It derives from <see cref="ITransactionalUnitOfWork"/> because it is one: what it adds is that a
/// caller can ask whether a unit is already open, which is what a retrying component needs to know.
/// Retrying a nested unit inside an open transaction cannot work — the store has typically aborted
/// the transaction with the failure — so only the outermost unit may be retried, and only as a whole.
/// It lives in <c>Bsft.Core</c> for the same reason the two unit-of-work contracts do: the mediator's
/// behaviors consult it without referencing a persistence package.
/// </para>
/// <para>
/// An implementation is scoped: one transaction per scope, never shared across scopes.
/// </para>
/// </remarks>
public interface IAmbientTransaction : ITransactionalUnitOfWork
{
    /// <summary>
    /// Gets whether a unit is in progress in this scope and asynchronous flow, so that a unit started
    /// now joins it.
    /// </summary>
    /// <remarks>
    /// A scope runs one unit of work at a time; a unit another flow of the same scope runs is not this
    /// flow's, and starting a second outermost unit beside it fails.
    /// </remarks>
    bool IsActive { get; }

    /// <summary>
    /// Reports whether a failure is one the store raised because of a conflict with a concurrent
    /// transaction — a serialization failure or a deadlock — so that re-running the whole outermost
    /// unit in a fresh transaction can succeed.
    /// </summary>
    /// <param name="exception">The failure the unit ended with.</param>
    /// <returns><see langword="true"/> when the store classifies the failure as such a conflict.</returns>
    /// <remarks>
    /// The classification belongs to the store's provider, because only the provider knows which
    /// error codes mean "try the whole transaction again". An implementation without such knowledge
    /// answers <see langword="false"/>. The answer stays available after the unit ended, also once
    /// the scope the transaction belongs to is disposed, because a retry decides after the attempt.
    /// </remarks>
    bool IsTransactionConflict(Exception exception);

    /// <summary>
    /// Returns the failure behind <paramref name="exception"/> that the store classifies as a conflict
    /// with a concurrent transaction: the exception itself, one of its inner exceptions, or a failure the
    /// unit recorded and the caller swallowed.
    /// </summary>
    /// <param name="exception">The failure the unit ended with.</param>
    /// <returns>
    /// The conflicting failure, or <see langword="null"/> when <see cref="IsTransactionConflict"/> is
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// A retrying component uses it to report why it retried — the conflict's error code rather than
    /// the "current transaction is aborted" the handler failed with afterwards. The default answers
    /// with <paramref name="exception"/> itself whenever <see cref="IsTransactionConflict"/> says yes,
    /// so an implementation that knows no more keeps working unchanged.
    /// </remarks>
    Exception? FindTransactionConflict(Exception exception) =>
        IsTransactionConflict(exception) ? exception : null;
}
