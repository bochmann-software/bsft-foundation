using Bsft.Core.Observers;

namespace Bsft.Hosting.Tests.Fakes;

/// <summary>Counts the failures it is told about.</summary>
internal sealed class CountingFailureReporter : IObserverFailureReporter
{
    public int Reported { get; private set; }

    public void ReportObserverFailed(object observer, Exception failure) => Reported++;
}
