using System.Diagnostics;

namespace Bsft.Scheduling.Diagnostics;

/// <summary>
/// Tracks the duration and outcome of a single job execution and records it through its owning
/// <see cref="JobTelemetry"/> on disposal. Owns the optional <see cref="Activity"/> for the run.
/// </summary>
internal sealed class JobExecutionTelemetryScope : IJobExecutionTelemetryScope
{
    private const string OutcomeSucceeded = "succeeded";
    private const string OutcomeFailed = "failed";

    private readonly JobTelemetry _telemetry;
    private readonly Activity? _activity;
    private readonly string _jobName;
    private readonly long _startTimestamp;

    // Failure is the default so that a scope unwound by an exception is recorded as one without the
    // caller needing a catch block purely for telemetry.
    private string _outcome = OutcomeFailed;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobExecutionTelemetryScope"/> class.
    /// </summary>
    /// <param name="telemetry">The owning telemetry that records the measurement.</param>
    /// <param name="activity">The execution activity, or <see langword="null"/> when not sampled.</param>
    /// <param name="jobName">The job name used as the metric tag.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public JobExecutionTelemetryScope(JobTelemetry telemetry, Activity? activity, string jobName)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _activity = activity;
        _jobName = jobName ?? throw new ArgumentNullException(nameof(jobName));
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    /// <inheritdoc />
    public void Succeeded()
    {
        _outcome = OutcomeSucceeded;
        _activity?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>Records the execution duration with its outcome, then disposes the activity.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (string.Equals(_outcome, OutcomeFailed, StringComparison.Ordinal))
            _activity?.SetStatus(ActivityStatusCode.Error);

        var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
        _telemetry.RecordExecution(_jobName, _outcome, elapsed.TotalSeconds);
        _activity?.Dispose();
    }
}
