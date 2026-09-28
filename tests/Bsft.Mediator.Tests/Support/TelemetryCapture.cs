using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Bsft.Mediator.Tests.Support;

/// <summary>
/// Test helper that captures activities and metric measurements emitted by a named source.
/// </summary>
internal sealed class TelemetryCapture : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;

    public TelemetryCapture(string sourceName)
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
        _meterListener.SetMeasurementEventCallback<long>(OnLong);
        _meterListener.SetMeasurementEventCallback<double>(OnDouble);
        _meterListener.Start();
    }

    public List<Activity> Activities { get; }

    public List<MetricMeasurement> Measurements { get; }

    private void OnLong(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
        Measurements.Add(new MetricMeasurement(instrument.Name, value, TagValue(tags, "outcome")));

    private void OnDouble(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
        Measurements.Add(new MetricMeasurement(instrument.Name, value, TagValue(tags, "outcome")));

    private static string? TagValue(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
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
