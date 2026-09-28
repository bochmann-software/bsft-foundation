using System.Diagnostics.Metrics;
using Bsft.Data.Diagnostics;
using Bsft.Instrumentation;
using Bsft.Instrumentation.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.Diagnostics;

public class DataTelemetryTests : IDisposable
{
    // The Bsft.Data meter is written to by the search tests as well, and those classes run in
    // their own collections beside this one. Every one of their measurements reaches this callback
    // on its own thread, so the list is written to concurrently and an unsynchronised List<T>.Add
    // loses entries — including, on one run, the duration this class had just recorded itself.
    private readonly object _gate = new();
    private readonly List<(string Instrument, double Value, string? Group)> _measurements = [];
    private readonly MeterListener _listener;

    public DataTelemetryTests()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Bsft.Data")
                    listener.EnableMeasurementEvents(instrument);
            },
        };
        _listener.SetMeasurementEventCallback<double>(Record);
        _listener.Start();
    }

    [Fact]
    public void ACompletedSearchRecordsDurationAndResultCount()
    {
        var telemetry = BuildTelemetry();

        telemetry.SearchCompleted("profile", 0.25, 42);

        var recorded = Recorded();

        recorded.Should().Contain(m => m.Instrument == "bsft.data.search.duration" && m.Group == "profile");
        recorded.Should().Contain(m => m.Instrument == "bsft.data.search.results" && m.Value == 42 && m.Group == "profile");
    }

    [Fact]
    public void TheInstrumentationSourceProviderIsRequired()
    {
        FluentActions.Invoking(() => new DataTelemetry(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("sources");
    }

    /// <summary>Takes a snapshot of what has been recorded so far, so an assertion never enumerates
    /// the list while another test's measurement is being added to it.</summary>
    private IReadOnlyList<(string Instrument, double Value, string? Group)> Recorded()
    {
        lock (_gate)
        {
            return [.. _measurements];
        }
    }

    private static IDataTelemetry BuildTelemetry()
    {
        var services = new ServiceCollection();
        services.AddBsftInstrumentationSource("Bsft.Data");
        services.AddSingleton<IDataTelemetry, DataTelemetry>();
        return services.BuildServiceProvider().GetRequiredService<IDataTelemetry>();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        string? group = null;
        foreach (var tag in tags)
        {
            if (tag.Key == "search.group")
                group = tag.Value?.ToString();
        }

        lock (_gate)
        {
            _measurements.Add((instrument.Name, value, group));
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
        GC.SuppressFinalize(this);
    }
}
