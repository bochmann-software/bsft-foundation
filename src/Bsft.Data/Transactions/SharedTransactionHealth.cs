using System.Data.Common;
using Bsft.Core.Persistence;

namespace Bsft.Data.Transactions;

/// <summary>
/// What the providers say about the scope's shared transaction: whether a failure was a conflict with a
/// concurrent transaction, and whether the store already aborted the open one.
/// </summary>
internal sealed class SharedTransactionHealth
{
    private readonly SharedTransactionEnlistment _enlistment;
    private readonly ITransactionConflictClassifier[] _classifiers;
    private readonly ITransactionAbortProbe[] _probes;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedTransactionHealth"/> class.
    /// </summary>
    /// <param name="enlistment">The scope's open transaction and the first command failure inside it.</param>
    /// <param name="classifiers">The providers' classifications of transaction conflicts.</param>
    /// <param name="probes">The providers' probes asking the store whether it aborted the transaction.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public SharedTransactionHealth(
        SharedTransactionEnlistment enlistment,
        IEnumerable<ITransactionConflictClassifier> classifiers,
        IEnumerable<ITransactionAbortProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(enlistment);
        ArgumentNullException.ThrowIfNull(classifiers);
        ArgumentNullException.ThrowIfNull(probes);

        _enlistment = enlistment;
        _classifiers = [.. classifiers];
        _probes = [.. probes];
    }

    /// <summary>Gets whether a provider registered a probe, so <see cref="ThrowIfAbortedAsync"/> asks the store.</summary>
    public bool HasAbortProbes => _probes.Length > 0;

    /// <summary>
    /// Reports whether <paramref name="exception"/> or one of its inner exceptions, or the first command
    /// failure of the last unit, is a conflict with a concurrent transaction according to any provider.
    /// </summary>
    /// <param name="exception">The failure the unit ended with.</param>
    /// <returns><see langword="true"/> when a provider calls either of them a conflict.</returns>
    public bool IsConflict(Exception exception) => FindConflict(exception) is not null;

    /// <summary>
    /// Returns the link of <paramref name="exception"/>'s chain, or else of the first command failure of
    /// the last unit, that a provider classifies as a conflict with a concurrent transaction.
    /// </summary>
    /// <remarks>
    /// The chain is walked here as well as in the providers' classifiers, so a classifier that inspects
    /// only the exception it is given still recognises a conflict the shared transaction reports as the
    /// inner exception of an <see cref="AmbientTransactionMisuseException"/>.
    /// </remarks>
    /// <param name="exception">The failure the unit ended with.</param>
    /// <returns>The conflicting failure, or <see langword="null"/> when there is none.</returns>
    public Exception? FindConflict(Exception exception) =>
        FindConflictInChain(exception)
        ?? (_enlistment.Failure is { } commandFailure ? FindConflictInChain(commandFailure) : null);

    /// <summary>Asks every classifier about <paramref name="exception"/> and each of its inner exceptions.</summary>
    private Exception? FindConflictInChain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var link = current;

            if (_classifiers.Any(classifier => classifier.IsConflict(link)))
                return link;
        }

        return null;
    }

    /// <summary>
    /// Throws when a database command failed or was cancelled inside the open transaction: the work
    /// completed anyway, so the failure was caught, and the store has typically aborted the transaction
    /// with it. The failure is the inner exception, so a provider's conflict classification still sees it.
    /// </summary>
    /// <exception cref="AmbientTransactionMisuseException">Thrown when a command failure was recorded.</exception>
    public void ThrowIfCommandFailed()
    {
        if (_enlistment.Failure is { } failure)
        {
            throw new AmbientTransactionMisuseException(
                "A database command failed or was cancelled inside the shared transaction, so the transaction cannot commit. The work completed anyway, which means the failure was caught and not rethrown; the whole transaction is rolled back instead.",
                failure);
        }
    }

    /// <summary>
    /// Throws when a provider's probe reports that the store already aborted <paramref name="transaction"/>,
    /// so its commit would silently roll back.
    /// </summary>
    /// <param name="transaction">The open shared transaction.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <exception cref="AmbientTransactionMisuseException">Thrown when the store aborted the transaction.</exception>
    public async Task ThrowIfAbortedAsync(DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        foreach (var probe in _probes)
        {
            var abort = await probe.FindAbortAsync(transaction, cancellationToken).ConfigureAwait(false);

            if (abort is not null)
            {
                throw new AmbientTransactionMisuseException(
                    "The store already aborted the shared transaction, so it cannot commit. No command failure reached the interceptor, which means a failure raised while query rows were read was caught and not rethrown; the whole transaction is rolled back instead.",
                    abort);
            }
        }
    }
}
