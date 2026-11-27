using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bsft.Data.Transactions;

/// <summary>
/// The contexts of a scope that take part in the shared transaction: the scope's instance of every
/// context type configured onto the shared connection, plus every context the transaction was handed
/// to, each as long as it is not disposed.
/// </summary>
/// <remarks>
/// Which context types take part is known only at run time, when <c>UseBsftSharedTransaction</c>
/// configures them, so they are resolved from the scope by type. Resolving them is what lets the unit
/// save a context that tracks a change but never ran a command, and clear it after a rollback.
/// </remarks>
internal sealed class SharedContextProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SharedTransactionEnlistment _enlistment;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedContextProvider"/> class.
    /// </summary>
    /// <param name="serviceProvider">The scope's provider, which the contexts are resolved from.</param>
    /// <param name="enlistment">The scope's recorded context types and enlisted contexts.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public SharedContextProvider(IServiceProvider serviceProvider, SharedTransactionEnlistment enlistment)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(enlistment);

        _serviceProvider = serviceProvider;
        _enlistment = enlistment;
    }

    /// <summary>Records the type of a context configured onto the shared connection.</summary>
    /// <param name="contextType">The type of the context.</param>
    public void Record(Type contextType) => _enlistment.Record(contextType);

    /// <summary>Resolves every context of the scope that takes part in the shared transaction.</summary>
    /// <returns>The contexts, each once, without the disposed ones.</returns>
    public IReadOnlyList<DbContext> GetContexts() =>
        _enlistment.ContextTypes
            .Select(_serviceProvider.GetService)
            .OfType<DbContext>()
            .Concat(_enlistment.Contexts)
            .Distinct()
            .Where(context => !_enlistment.IsDisposed(context))
            .ToList();

    /// <summary>
    /// Makes <paramref name="transaction"/> the scope's open one and hands it to every context the
    /// scope already has; a context resolved later takes it as it initializes.
    /// </summary>
    /// <param name="transaction">The transaction the outermost unit opened.</param>
    /// <exception cref="Bsft.Core.Persistence.AmbientTransactionMisuseException">
    /// Thrown when a context retries on failure through its own execution strategy.
    /// </exception>
    public void Begin(DbTransaction transaction)
    {
        _enlistment.Begin(transaction);

        foreach (var context in GetContexts())
            _enlistment.Enlist(context);
    }

    /// <summary>
    /// Forgets what the change trackers of every context still describe, after a unit that did not
    /// commit: the rows they track were rolled back.
    /// </summary>
    public void ClearChangeTrackers()
    {
        foreach (var context in GetContexts())
            context.ChangeTracker.Clear();
    }

    /// <summary>Takes the ended transaction back from every context that uses it.</summary>
    /// <param name="transaction">The ended transaction.</param>
    public void Release(DbTransaction transaction)
    {
        foreach (var context in GetContexts())
        {
            // Disposing EF Core's wrapper of a transaction it did not begin rolls nothing back: it
            // detaches the transaction and gives back the hold on the connection that UseTransaction
            // took, so a command the context runs after the unit closes the connection it opens.
            if (context.Database.CurrentTransaction is { } current && current.GetDbTransaction() == transaction)
                current.Dispose();
        }

        _enlistment.End();
    }
}
