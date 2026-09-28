using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Bsft.Instrumentation;

/// <summary>
/// Default <see cref="IInstrumentationSource"/> implementation backed by a dedicated
/// <see cref="ActivitySource"/> instance and an <see cref="Meter"/> obtained from an
/// <see cref="IMeterFactory"/>. Instruments are created once and cached by name.
/// </summary>
internal sealed class InstrumentationSource : IInstrumentationSource, IDisposable
{
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private readonly ConcurrentDictionary<string, Counter<long>> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Histogram<double>> _histograms = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="InstrumentationSource"/> class.
    /// </summary>
    /// <param name="name">The source name (used for both the activity source and meter).</param>
    /// <param name="meterFactory">The factory used to create the meter.</param>
    public InstrumentationSource(string name, IMeterFactory meterFactory)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(ObservabilitySourceRegistration.NameRequiredMessage, nameof(name));
        if (meterFactory is null)
            throw new ArgumentNullException(nameof(meterFactory));

        Name = name;
        _activitySource = new ActivitySource(name);
        _meter = meterFactory.Create(name);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Activity name must be provided.", nameof(name));

        return _activitySource.StartActivity(name, kind);
    }

    /// <inheritdoc />
    public Counter<long> GetOrCreateCounter(string name, string? unit = null) =>
        _counters.GetOrAdd(name, static (n, state) => state.Meter.CreateCounter<long>(n, state.Unit), (Meter: _meter, Unit: unit));

    /// <inheritdoc />
    public Histogram<double> GetOrCreateHistogram(string name, string? unit = null) =>
        _histograms.GetOrAdd(name, static (n, state) => state.Meter.CreateHistogram<double>(n, state.Unit), (Meter: _meter, Unit: unit));

    /// <summary>Disposes the underlying activity source. The meter is owned by the meter factory.</summary>
    public void Dispose() => _activitySource.Dispose();
}
