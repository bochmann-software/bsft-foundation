using System.Data.Common;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Bsft.Data.Transactions;

/// <summary>
/// Runs the shared transaction's own connection steps between the scope's
/// <see cref="ISharedTransactionStepObserver"/> notifications, with the exception semantics the observer
/// contract documents.
/// </summary>
/// <remarks>
/// Without a registered observer every step is just awaited: no notification, no catch, no log. The catch-all
/// blocks below are the observer boundary: an observer is a host type this assembly cannot name the
/// exceptions of, and each caught exception is logged as a warning, never dropped; a refusal of a step that can
/// be left out is not caught at all and propagates unchanged.
/// </remarks>
internal sealed class SharedTransactionStepReporter
{
    private readonly ISharedTransactionStepObserver[] _observers;
    private readonly ILogger<SharedTransactionStepReporter> _logger;

    /// <summary>Initializes a new instance of the <see cref="SharedTransactionStepReporter"/> class.</summary>
    /// <param name="observers">The scope's observers, told in this order.</param>
    /// <param name="logger">Records an observer exception that is not thrown.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public SharedTransactionStepReporter(
        IEnumerable<ISharedTransactionStepObserver> observers, ILogger<SharedTransactionStepReporter> logger)
    {
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(logger);

        _observers = [.. observers];
        _logger = logger;
    }

    /// <summary>
    /// Runs a step an observer may refuse: an exception from <see cref="ISharedTransactionStepObserver.OnStepStarting"/>
    /// propagates unchanged and the step does not run.
    /// </summary>
    /// <param name="kind">What the step does.</param>
    /// <param name="connection">The shared connection.</param>
    /// <param name="step">The step, given <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">A cancellation token the step observes.</param>
    /// <returns>A task that completes when the step and its notifications ended.</returns>
    public async Task RunAsync(
        SharedTransactionStepKind kind,
        DbConnection connection,
        Func<CancellationToken, Task> step,
        CancellationToken cancellationToken = default) =>
        await RunAsync(kind, connection, async token =>
        {
            await step(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Runs a step with a result an observer may refuse: an exception from
    /// <see cref="ISharedTransactionStepObserver.OnStepStarting"/> propagates unchanged and the step does not run.
    /// </summary>
    /// <typeparam name="TResult">The step's result.</typeparam>
    /// <param name="kind">What the step does.</param>
    /// <param name="connection">The shared connection.</param>
    /// <param name="step">The step, given <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">A cancellation token the step observes.</param>
    /// <returns>The step's result.</returns>
    public async Task<TResult> RunAsync<TResult>(
        SharedTransactionStepKind kind,
        DbConnection connection,
        Func<CancellationToken, Task<TResult>> step,
        CancellationToken cancellationToken = default)
    {
        var notice = new SharedTransactionStepInfo(kind, connection);
        var startedCount = StartRefusable(notice);

        try
        {
            return await step(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Complete(notice, _observers.AsSpan(0, startedCount));
        }
    }

    /// <summary>
    /// Runs a step that ends the unit and therefore runs even when an observer refuses it; every refusal is
    /// logged as a warning.
    /// </summary>
    /// <remarks>
    /// A refusal is never the unit's exception: after a commit, throwing would report a persisted unit as
    /// failed and let a retry write it twice, and a unit that did not commit already fails with an exception
    /// of its own.
    /// </remarks>
    /// <param name="kind">What the step does.</param>
    /// <param name="connection">The shared connection.</param>
    /// <param name="step">The step, given <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">A cancellation token the step observes.</param>
    /// <returns>A task that completes when the step and its notifications ended.</returns>
    public async Task RunEndingAsync(
        SharedTransactionStepKind kind,
        DbConnection connection,
        Func<CancellationToken, Task> step,
        CancellationToken cancellationToken = default)
    {
        var notice = new SharedTransactionStepInfo(kind, connection);
        var started = new List<ISharedTransactionStepObserver>(_observers.Length);

        foreach (var observer in _observers)
        {
            try
            {
                observer.OnStepStarting(notice);
                started.Add(observer);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "An observer refused the {Step} step of the shared transaction, which runs anyway because it ends the unit; the refusal is logged instead of thrown.",
                    kind);
            }
        }

        try
        {
            await step(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Complete(notice, CollectionsMarshal.AsSpan(started));
        }
    }

    /// <summary>
    /// Tells the observers in order that the step starts; when one throws, completes the ones already told
    /// and lets the exception propagate.
    /// </summary>
    private int StartRefusable(SharedTransactionStepInfo notice)
    {
        var startedCount = 0;

        try
        {
            for (; startedCount < _observers.Length; startedCount++)
                _observers[startedCount].OnStepStarting(notice);
        }
        finally
        {
            if (startedCount < _observers.Length)
                Complete(notice, _observers.AsSpan(0, startedCount));
        }

        return startedCount;
    }

    /// <summary>Tells the started observers, in reverse order, that the step ended; logs what they throw.</summary>
    private void Complete(SharedTransactionStepInfo notice, ReadOnlySpan<ISharedTransactionStepObserver> started)
    {
        for (var index = started.Length - 1; index >= 0; index--)
        {
            try
            {
                started[index].OnStepCompleted(notice);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "An observer of the shared transaction failed after the {Step} step had ended; the failure is ignored.",
                    notice.Kind);
            }
        }
    }
}
