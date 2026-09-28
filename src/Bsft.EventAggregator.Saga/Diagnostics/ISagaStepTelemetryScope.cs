namespace Bsft.EventAggregator.Saga.Diagnostics;

/// <summary>
/// A per-step telemetry scope. Disposal records the step duration with its outcome
/// (executed or skipped).
/// </summary>
public interface ISagaStepTelemetryScope : IDisposable
{
    /// <summary>Marks the step as skipped because its condition was not met.</summary>
    void Skipped();
}
