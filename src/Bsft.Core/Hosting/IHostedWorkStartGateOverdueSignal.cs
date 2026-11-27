namespace Bsft.Core.Hosting;

/// <summary>
/// The optional signal a start gate gives when it has been pending for longer than it should be, so that
/// a health check can report a worker that is stuck waiting instead of working.
/// </summary>
public interface IHostedWorkStartGateOverdueSignal
{
    /// <summary>Says whether the gate is still pending after the time it was expected to take.</summary>
    /// <returns><c>true</c> when the gate is overdue, <c>false</c> when it is open or still within its time.</returns>
    bool IsOverdue();
}
