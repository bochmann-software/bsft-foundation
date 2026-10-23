namespace Bsft.Core.Observers;

/// <summary>
/// Receives the failures an <see cref="IObserverGuard"/> contained, so the host can phrase them in
/// its own words and at its own level.
/// </summary>
/// <remarks>
/// The guard owns the control flow, the reporter owns the vocabulary, the way
/// <see cref="Polling.IPollReporter"/> does for the poll loop. It also keeps <c>Bsft.Core</c> free of
/// a logging dependency: <c>Bsft.Hosting</c> supplies a reporter that logs.
/// </remarks>
public interface IObserverFailureReporter
{
    /// <summary>Reports an observer call that threw. The observed operation carries on.</summary>
    /// <param name="observer">The observer whose call failed.</param>
    /// <param name="failure">What the call threw.</param>
    void ReportObserverFailed(object observer, Exception failure);
}
