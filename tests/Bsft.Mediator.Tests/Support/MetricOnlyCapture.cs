using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Bsft.Mediator.Tests.Support;

/// <summary>
/// Captures only metric measurements for a named meter, deliberately without an activity listener so
/// activities for the source are not sampled (StartActivity returns null).
/// </summary>
internal sealed class MetricOnlyCapture : IDisposable
{
    private readonly MeterListener _meterListener;
    private int _requestCount;

    public MetricOnlyCapture(string meterName)
    {
        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == meterName && instrument.Name == "bsft.mediator.requests")
                    listener.EnableMeasurementEvents(instrument);
            },
        };
        _meterListener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref _requestCount, (int)value));
        _meterListener.Start();
    }

    public int RequestCount => _requestCount;

    public void Dispose() => _meterListener.Dispose();
}
