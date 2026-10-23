namespace Bsft.Core.Observers;

/// <summary>
/// The default <see cref="IObserverGuard"/>: contains every failure of an observer call, counts it
/// and hands it to an <see cref="IObserverFailureReporter"/>.
/// </summary>
/// <remarks>
/// <para>
/// The guard exists for one principle: an observer never fails the operation it watches. Its
/// failure is contained, the count is raised before anything else runs, and the observed operation
/// and the other observers go on.
/// </para>
/// <para>
/// A reporter that throws is contained too. The observer failure was already counted, and a
/// reporter that cannot report must not turn the failure into the operation's; it is counted in
/// <see cref="ReporterFailures"/>, so that a host can tell failures that were never reported.
/// </para>
/// </remarks>
public sealed class ObserverGuard : IObserverGuard
{
    private readonly IObserverFailureReporter _reporter;
    private long _failures;
    private long _reporterFailures;

    /// <summary>Initializes a new instance of the <see cref="ObserverGuard"/> class.</summary>
    /// <param name="reporter">Receives each contained failure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reporter"/> is null.</exception>
    public ObserverGuard(IObserverFailureReporter reporter)
    {
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
    }

    /// <inheritdoc />
    public long Failures => Interlocked.Read(ref _failures);

    /// <inheritdoc />
    public long ReporterFailures => Interlocked.Read(ref _reporterFailures);

    /// <inheritdoc />
    public void Invoke(object observer, Action call)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(call);

        try
        {
            call();
        }
        catch (Exception failure)
        {
            Contain(observer, failure);
        }
    }

    /// <inheritdoc />
    public async Task InvokeAsync(object observer, Func<CancellationToken, Task> call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(call);

        try
        {
            await call(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            Contain(observer, failure);
        }
    }

    private void Contain(object observer, Exception failure)
    {
        Interlocked.Increment(ref _failures);
        try
        {
            _reporter.ReportObserverFailed(observer, failure);
        }
        catch (Exception)
        {
            // The failure is counted above; a broken reporter has nobody left to tell, so the
            // reporter's own failure is counted and nothing else.
            Interlocked.Increment(ref _reporterFailures);
        }
    }
}
