using System.Diagnostics;
using Bsft.Core;

namespace Bsft.Mediator;

/// <summary>
/// Tracks the duration and outcome of a single mediator dispatch and records them through its owning
/// <see cref="MediatorTelemetry"/> on disposal. Owns the optional <see cref="Activity"/> created for
/// the dispatch.
/// </summary>
internal sealed class RequestTelemetryScope : IRequestTelemetryScope
{
    private const string OutcomeSuccess = "success";
    private const string OutcomeExpectedError = "expected_error";
    private const string OutcomeError = "error";

    private readonly MediatorTelemetry _telemetry;
    private readonly Activity? _activity;
    private readonly string _requestName;
    private readonly long _startTimestamp;
    private string _outcome = OutcomeSuccess;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestTelemetryScope"/> class.
    /// </summary>
    /// <param name="telemetry">The owning telemetry that records the measurements.</param>
    /// <param name="activity">The activity for the dispatch, or <see langword="null"/> when not sampled.</param>
    /// <param name="requestName">The request name used as the metric outcome tag.</param>
    public RequestTelemetryScope(MediatorTelemetry telemetry, Activity? activity, string requestName)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _activity = activity;
        _requestName = requestName;
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    /// <inheritdoc />
    public void Complete() => _outcome = OutcomeSuccess;

    /// <inheritdoc />
    public void Fail(Exception exception)
    {
        if (exception is null)
            throw new ArgumentNullException(nameof(exception));

        // Expected framework exceptions (validation, business-rule, not-found, ...) are normal control
        // flow, not infrastructure failures, so they must not inflate error rates or span error status.
        if (IsExpected(exception))
        {
            _outcome = OutcomeExpectedError;
            return;
        }

        _outcome = OutcomeError;

        // Only the exception type is placed on the span status (a concise description per the OTel
        // spec); the full message stays in the structured exception event, avoiding duplication of
        // potentially sensitive runtime detail onto the span status itself.
        //
        // exception.message follows the OTel exception semantic convention and may contain sensitive
        // runtime detail (the trace backend must therefore be treated as a sensitive sink). Redaction,
        // if required, belongs in the export pipeline (an OTel processor in Bsft.Observability), not in
        // this vendor-neutral emit layer.
        _activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
        _activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            ["exception.type"] = exception.GetType().FullName,
            ["exception.message"] = exception.Message,
        }));
    }

    // A notification publish surfaces handler failures as an AggregateException; treat it as expected
    // only when every wrapped failure is itself an expected framework exception.
    private static bool IsExpected(Exception exception) => exception switch
    {
        BsftException => true,
        AggregateException aggregate =>
            aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(inner => inner is BsftException),
        _ => false,
    };

    /// <summary>Records duration and request count with the final outcome, then disposes the activity.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
        _telemetry.Record(_requestName, _outcome, elapsed.TotalSeconds, _outcome == OutcomeError);
        _activity?.Dispose();
    }
}
