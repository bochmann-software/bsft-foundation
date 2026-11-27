using Bsft.Core.Observers;

namespace Bsft.Hosting;

/// <summary>
/// The reporter of the guard a host without <c>AddBsftObservers</c> gets: a failure is contained and not reported.
/// </summary>
internal sealed class SilentObserverFailureReporter : IObserverFailureReporter
{
    /// <inheritdoc />
    public void ReportObserverFailed(object observer, Exception failure)
    {
        // There is nowhere to report to: the host registered no reporter, and the failing party may be the logger itself.
    }
}
