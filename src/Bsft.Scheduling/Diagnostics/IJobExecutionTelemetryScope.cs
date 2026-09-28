namespace Bsft.Scheduling.Diagnostics;

/// <summary>
/// A telemetry scope covering one job execution. The outcome defaults to a failure so that a scope
/// unwound by an exception is recorded correctly without the caller having to catch it.
/// </summary>
public interface IJobExecutionTelemetryScope : IDisposable
{
    /// <summary>Marks the execution as successful.</summary>
    void Succeeded();
}
