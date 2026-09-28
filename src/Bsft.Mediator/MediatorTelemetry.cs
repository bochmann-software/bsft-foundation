using System.Diagnostics;
using System.Diagnostics.Metrics;
using Bsft.Instrumentation;

namespace Bsft.Mediator;

/// <summary>
/// Default <see cref="IMediatorTelemetry"/> implementation that emits traces and metrics through the
/// vendor-neutral "Bsft.Mediator" instrumentation source.
/// </summary>
internal sealed class MediatorTelemetry : IMediatorTelemetry
{
    /// <summary>The activity source and meter name for mediator telemetry.</summary>
    internal const string SourceName = "Bsft.Mediator";

    private const string RequestDuration = "bsft.mediator.request.duration";
    private const string Requests = "bsft.mediator.requests";
    private const string Failures = "bsft.mediator.failures";

    private readonly IInstrumentationSource _source;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _requests;
    private readonly Counter<long> _failures;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorTelemetry"/> class.
    /// </summary>
    /// <param name="sources">The instrumentation source provider.</param>
    public MediatorTelemetry(IInstrumentationSourceProvider sources)
    {
        if (sources is null)
            throw new ArgumentNullException(nameof(sources));

        _source = sources.Get(SourceName);
        _duration = _source.GetOrCreateHistogram(RequestDuration, "s");
        _requests = _source.GetOrCreateCounter(Requests);
        _failures = _source.GetOrCreateCounter(Failures);
    }

    /// <inheritdoc />
    public IRequestTelemetryScope BeginRequest(string requestName, string requestType, string? correlationId)
    {
        var activity = _source.StartActivity(requestName);
        if (activity is not null)
        {
            activity.SetTag("bsft.request_type", requestType);
            activity.SetTag("bsft.request_name", requestName);
            if (CorrelationIds.IsWellFormed(correlationId))
                activity.SetTag("correlation_id", correlationId);
        }

        return new RequestTelemetryScope(this, activity, requestName);
    }

    /// <inheritdoc />
    public IRequestTelemetryScope BeginPublish(string notificationName, int handlerCount)
    {
        var activity = _source.StartActivity(notificationName);
        if (activity is not null)
        {
            activity.SetTag("bsft.notification_name", notificationName);
            activity.SetTag("bsft.handler_count", handlerCount);
        }

        return new RequestTelemetryScope(this, activity, notificationName);
    }

    /// <summary>
    /// Records the outcome of a completed dispatch: the request count and duration, plus a failure
    /// count for unexpected errors.
    /// </summary>
    /// <param name="requestName">The request or notification name.</param>
    /// <param name="outcome">The outcome tag value.</param>
    /// <param name="seconds">The elapsed duration in seconds.</param>
    /// <param name="recordFailure">Whether to increment the failure counter.</param>
    internal void Record(string requestName, string outcome, double seconds, bool recordFailure)
    {
        var tags = new TagList
        {
            { "request_name", requestName },
            { "outcome", outcome },
        };

        _requests.Add(1, tags);
        _duration.Record(seconds, tags);

        if (recordFailure)
            _failures.Add(1, new KeyValuePair<string, object?>("request_name", requestName));
    }
}
