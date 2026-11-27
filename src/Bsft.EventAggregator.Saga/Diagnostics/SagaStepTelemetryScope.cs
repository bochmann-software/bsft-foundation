using System.Diagnostics;

namespace Bsft.EventAggregator.Saga.Diagnostics;

/// <summary>
/// Tracks the duration and outcome of a single saga step and records it through its owning
/// <see cref="SagaTelemetry"/> on disposal. Owns the optional <see cref="Activity"/> for the step.
/// </summary>
internal sealed class SagaStepTelemetryScope : ISagaStepTelemetryScope
{
    private const string ExecutedOutcome = "executed";
    private const string SkippedOutcome = "skipped";

    private readonly SagaTelemetry _telemetry;
    private readonly TimeProvider _timeProvider;
    private readonly Activity? _activity;
    private readonly string _sagaType;
    private readonly long _startTimestamp;
    private string _outcome = ExecutedOutcome;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaStepTelemetryScope"/> class.
    /// </summary>
    /// <param name="telemetry">The owning telemetry that records the measurement.</param>
    /// <param name="timeProvider">The clock the step's duration is measured on.</param>
    /// <param name="activity">The step activity, or <see langword="null"/> when not sampled.</param>
    /// <param name="sagaType">The saga type identifier used as the metric tag.</param>
    public SagaStepTelemetryScope(SagaTelemetry telemetry, TimeProvider timeProvider, Activity? activity, string sagaType)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _activity = activity;
        _sagaType = sagaType ?? throw new ArgumentNullException(nameof(sagaType));
        _startTimestamp = _timeProvider.GetTimestamp();
    }

    /// <inheritdoc />
    public void Skipped()
    {
        _outcome = SkippedOutcome;
        _activity?.SetTag("saga.skipped", true);
    }

    /// <summary>Records the step duration with its outcome, then disposes the activity.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        var elapsed = _timeProvider.GetElapsedTime(_startTimestamp);
        _telemetry.RecordStep(_sagaType, _outcome, elapsed.TotalSeconds);
        _activity?.Dispose();
    }
}
