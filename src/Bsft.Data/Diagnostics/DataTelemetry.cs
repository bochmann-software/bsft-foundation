using System.Diagnostics;
using System.Diagnostics.Metrics;
using Bsft.Instrumentation;

namespace Bsft.Data.Diagnostics;

/// <summary>
/// Default <see cref="IDataTelemetry"/> implementation emitting through the vendor-neutral
/// "Bsft.Data" instrumentation source.
/// </summary>
internal sealed class DataTelemetry : IDataTelemetry
{
    /// <summary>The activity source and meter name for data telemetry.</summary>
    internal const string SourceName = "Bsft.Data";

    private const string SearchDuration = "bsft.data.search.duration";
    private const string SearchResults = "bsft.data.search.results";
    private const string GroupTag = "search.group";

    private readonly Histogram<double> _duration;
    private readonly Histogram<double> _results;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataTelemetry"/> class.
    /// </summary>
    /// <param name="sources">The instrumentation source provider.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources"/> is null.</exception>
    public DataTelemetry(IInstrumentationSourceProvider sources)
    {
        if (sources is null)
            throw new ArgumentNullException(nameof(sources));

        var source = sources.Get(SourceName);
        _duration = source.GetOrCreateHistogram(SearchDuration, "s");
        _results = source.GetOrCreateHistogram(SearchResults);
    }

    /// <inheritdoc />
    public void SearchCompleted(string group, double seconds, int resultCount)
    {
        var tags = new TagList { { GroupTag, group } };

        _duration.Record(seconds, tags);
        _results.Record(resultCount, tags);
    }
}
