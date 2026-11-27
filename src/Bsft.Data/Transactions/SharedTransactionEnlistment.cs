using System.Data.Common;
using Bsft.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bsft.Data.Transactions;

/// <summary>
/// The open shared transaction of a scope and the contexts that take part in it: hands the
/// transaction to a context as it initializes or is about to run a command or save, and forgets a
/// context once it is disposed.
/// </summary>
internal sealed class SharedTransactionEnlistment
{
    private readonly HashSet<Type> _contextTypes = [];
    private readonly List<DbContext> _contexts = [];
    private readonly HashSet<DbContext> _disposedContexts = [];

    /// <summary>Gets the open transaction, or <see langword="null"/> while no unit is open.</summary>
    public DbTransaction? Transaction { get; private set; }

    /// <summary>
    /// Gets the first database command failure or cancellation of the open or last transaction, or
    /// <see langword="null"/> while there was none. It is kept after the unit ended, so the failed
    /// unit's retry decision can still read it, and forgotten when the next unit begins.
    /// </summary>
    public Exception? Failure { get; private set; }

    /// <summary>Gets the contexts the open transaction was handed to and that are not disposed.</summary>
    public IReadOnlyList<DbContext> Contexts => _contexts;

    /// <summary>Gets the types of the scope's contexts configured onto the shared connection.</summary>
    public IReadOnlyCollection<Type> ContextTypes => _contextTypes;

    /// <summary>Records the type of a context configured onto the shared connection.</summary>
    /// <param name="contextType">The type of the context.</param>
    public void Record(Type contextType) => _contextTypes.Add(contextType);

    /// <summary>Makes <paramref name="transaction"/> the one contexts enlist in from now on.</summary>
    /// <param name="transaction">The transaction the outermost unit opened.</param>
    public void Begin(DbTransaction transaction)
    {
        Transaction = transaction;
        Failure = null;
    }

    /// <summary>
    /// Records that a command of an enlisted context failed or was cancelled while the transaction was
    /// open; the first one is kept. Does nothing while no unit is open.
    /// </summary>
    /// <param name="failure">The command's exception.</param>
    public void RecordFailure(Exception failure)
    {
        if (Transaction is not null)
            Failure ??= failure;
    }

    /// <summary>
    /// Hands the open transaction to a context that does not use it yet; does nothing when no unit
    /// is open, the context already uses it, or the context is disposed.
    /// </summary>
    /// <param name="context">The context initializing, or about to run a command or save.</param>
    /// <exception cref="AmbientTransactionMisuseException">
    /// Thrown when the context retries on failure through its own execution strategy.
    /// </exception>
    public void Enlist(DbContext context)
    {
        if (Transaction is null
            || IsDisposed(context)
            || context.Database.CurrentTransaction?.GetDbTransaction() == Transaction)
        {
            return;
        }

        // A retrying execution strategy refuses to run inside a transaction it did not open, and
        // retrying one statement of a shared transaction would be wrong anyway: the retry belongs
        // around the whole outermost unit, which is what [Retryable] does.
        if (context.Database.CreateExecutionStrategy().RetriesOnFailure)
        {
            throw new AmbientTransactionMisuseException(
                $"The DbContext '{context.GetType().Name}' is enlisted in the shared transaction but retries on failure through its own execution strategy (EnableRetryOnFailure). A shared transaction is retried as a whole by the mediator's [Retryable] instead; remove EnableRetryOnFailure from the context's options.");
        }

        context.Database.UseTransaction(Transaction);
        _contexts.Add(context);
    }

    /// <summary>
    /// Forgets a context that was disposed: its own disposal already gave the transaction back, and a
    /// disposed context can neither be saved nor cleared.
    /// </summary>
    /// <param name="context">The disposed context.</param>
    public void Forget(DbContext context)
    {
        _disposedContexts.Add(context);
        _contexts.Remove(context);
    }

    /// <summary>Reports whether <paramref name="context"/> was disposed.</summary>
    /// <param name="context">A context of the scope.</param>
    /// <returns><see langword="true"/> once the context reported its disposal.</returns>
    public bool IsDisposed(DbContext context) => _disposedContexts.Contains(context);

    /// <summary>Ends the unit: no context enlists any more until the next unit begins.</summary>
    public void End()
    {
        Transaction = null;
        _contexts.Clear();
    }
}
