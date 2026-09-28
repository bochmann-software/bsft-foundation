using System.Diagnostics;
using System.Diagnostics.Metrics;
using Bsft.Instrumentation;

namespace Bsft.EventAggregator.Saga.Diagnostics;

/// <summary>
/// Default <see cref="ISagaTelemetry"/> implementation that emits through the vendor-neutral
/// "Bsft.Saga" instrumentation source.
/// </summary>
internal sealed class SagaTelemetry : ISagaTelemetry
{
    /// <summary>The activity source and meter name for saga telemetry.</summary>
    internal const string SourceName = "Bsft.Saga";

    private const string StartedMetric = "bsft.saga.started";
    private const string CompletedMetric = "bsft.saga.completed";
    private const string StepDurationMetric = "bsft.saga.step.duration";
    private const string TimeoutScheduledMetric = "bsft.saga.timeout.scheduled";
    private const string TimeoutFiredMetric = "bsft.saga.timeout.fired";
    private const string TimeoutCanceledMetric = "bsft.saga.timeout.canceled";
    private const string TimeoutMissedMetric = "bsft.saga.timeout.missed";
    private const string TimeoutDeadLetteredMetric = "bsft.saga.timeout.dead_lettered";
    private const string SagaTypeTag = "saga.type";
    private const string TimeoutNameTag = "saga.timeout.name";
    private const string TimeoutDispatchActivity = "saga.timeout";

    private readonly IInstrumentationSource _source;
    private readonly Counter<long> _started;
    private readonly Counter<long> _completed;
    private readonly Histogram<double> _stepDuration;
    private readonly Counter<long> _timeoutScheduled;
    private readonly Counter<long> _timeoutFired;
    private readonly Counter<long> _timeoutCancelled;
    private readonly Counter<long> _timeoutMissed;
    private readonly Counter<long> _timeoutDeadLettered;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaTelemetry"/> class.
    /// </summary>
    /// <param name="sources">The instrumentation source provider.</param>
    public SagaTelemetry(IInstrumentationSourceProvider sources)
    {
        if (sources is null)
            throw new ArgumentNullException(nameof(sources));

        _source = sources.Get(SourceName);
        _started = _source.GetOrCreateCounter(StartedMetric);
        _completed = _source.GetOrCreateCounter(CompletedMetric);
        _stepDuration = _source.GetOrCreateHistogram(StepDurationMetric, "s");
        _timeoutScheduled = _source.GetOrCreateCounter(TimeoutScheduledMetric);
        _timeoutFired = _source.GetOrCreateCounter(TimeoutFiredMetric);
        _timeoutCancelled = _source.GetOrCreateCounter(TimeoutCanceledMetric);
        _timeoutMissed = _source.GetOrCreateCounter(TimeoutMissedMetric);
        _timeoutDeadLettered = _source.GetOrCreateCounter(TimeoutDeadLetteredMetric);
    }

    /// <inheritdoc />
    public void SagaStarted(string sagaType) =>
        _started.Add(1, new KeyValuePair<string, object?>(SagaTypeTag, sagaType));

    /// <inheritdoc />
    public void SagaCompleted(string sagaType) =>
        _completed.Add(1, new KeyValuePair<string, object?>(SagaTypeTag, sagaType));

    /// <inheritdoc />
    public void TimeoutScheduled(string sagaType) =>
        _timeoutScheduled.Add(1, new KeyValuePair<string, object?>(SagaTypeTag, sagaType));

    /// <inheritdoc />
    public void TimeoutFired(string sagaType) =>
        _timeoutFired.Add(1, new KeyValuePair<string, object?>(SagaTypeTag, sagaType));

    /// <inheritdoc />
    public void TimeoutCanceled(string sagaType) =>
        _timeoutCancelled.Add(1, new KeyValuePair<string, object?>(SagaTypeTag, sagaType));

    /// <inheritdoc />
    public void TimeoutMissed(string sagaType) =>
        _timeoutMissed.Add(1, new KeyValuePair<string, object?>(SagaTypeTag, sagaType));

    /// <inheritdoc />
    public void TimeoutDeadLettered(string sagaType) =>
        _timeoutDeadLettered.Add(1, new KeyValuePair<string, object?>(SagaTypeTag, sagaType));

    /// <inheritdoc />
    public IDisposable? BeginTimeoutDispatch(string sagaType, string timeoutName)
    {
        var activity = _source.StartActivity(TimeoutDispatchActivity);
        activity?.SetTag(SagaTypeTag, sagaType);
        activity?.SetTag(TimeoutNameTag, timeoutName);

        return activity;
    }

    /// <inheritdoc />
    public ISagaStepTelemetryScope BeginStep(string sagaType, string? correlationKey, int stepIndex)
    {
        var activity = _source.StartActivity("saga.step");
        if (activity is not null)
        {
            activity.SetTag(SagaTypeTag, sagaType);
            if (correlationKey is not null)
                activity.SetTag("saga.correlation_key", correlationKey);
            activity.SetTag("saga.step_index", stepIndex);
        }

        return new SagaStepTelemetryScope(this, activity, sagaType);
    }

    /// <summary>Records the duration of a completed step with its outcome.</summary>
    /// <param name="sagaType">The saga type identifier.</param>
    /// <param name="outcome">The step outcome tag value.</param>
    /// <param name="seconds">The elapsed step duration in seconds.</param>
    internal void RecordStep(string sagaType, string outcome, double seconds)
    {
        var tags = new TagList
        {
            { SagaTypeTag, sagaType },
            { "outcome", outcome },
        };

        _stepDuration.Record(seconds, tags);
    }
}
