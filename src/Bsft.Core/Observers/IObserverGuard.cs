namespace Bsft.Core.Observers;

/// <summary>
/// Keeps an observer from breaking the operation it observes: an observer call that throws is
/// reported and counted, and everything else goes on.
/// </summary>
public interface IObserverGuard
{
    /// <summary>Gets how many observer calls failed since this guard was created.</summary>
    long Failures { get; }

    /// <summary>
    /// Gets how many times the <see cref="IObserverFailureReporter"/> itself threw while reporting an
    /// observer failure since this guard was created. A non-zero value means failures are being
    /// counted but not reported.
    /// </summary>
    long ReporterFailures { get; }

    /// <summary>Runs one observer call; a throw is contained.</summary>
    /// <param name="observer">The observer, named in the report.</param>
    /// <param name="call">The call.</param>
    void Invoke(object observer, Action call);

    /// <summary>Runs one asynchronous observer call; a failure is contained.</summary>
    /// <param name="observer">The observer, named in the report.</param>
    /// <param name="call">The call; it receives <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">Cancels the observed operation.</param>
    /// <returns>A task that completes when the call has finished or its failure was contained.</returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled: that is the observed
    /// operation being cancelled, not an observer failing, so it is not contained.
    /// </exception>
    Task InvokeAsync(object observer, Func<CancellationToken, Task> call, CancellationToken cancellationToken = default);
}
