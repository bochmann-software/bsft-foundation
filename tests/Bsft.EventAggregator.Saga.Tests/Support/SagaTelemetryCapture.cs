using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Bsft.EventAggregator.Saga.Tests.Support;

/// <summary>Captures activities and metric measurements emitted by the Bsft.Saga source.</summary>
internal sealed class SagaTelemetryCapture : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;

    public SagaTelemetryCapture(string sourceName)
    {
        Activities = [];
        Measurements = [];

        _activityListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = Activities.Add,
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == sourceName)
                    listener.EnableMeasurementEvents(instrument);
            },
        };
        _meterListener.SetMeasurementEventCallback<long>(Record);
        _meterListener.SetMeasurementEventCallback<double>(Record);
        _meterListener.Start();
    }

    public List<Activity> Activities { get; }

    public List<SagaMeasurement> Measurements { get; }

    private void Record(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
        Measurements.Add(new SagaMeasurement(instrument.Name, value, Tag(tags, "outcome"), Tag(tags, "saga.type")));

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
        Measurements.Add(new SagaMeasurement(instrument.Name, value, Tag(tags, "outcome"), Tag(tags, "saga.type")));

    private static string? Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
                return tag.Value?.ToString();
        }

        return null;
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }
}
