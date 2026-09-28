using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Bsft.Instrumentation;

/// <summary>
/// Vendor-neutral instrumentation primitive: a named pairing of an <see cref="ActivitySource"/>
/// and a <see cref="Meter"/> for emitting traces and metrics. Obtain instances from
/// <see cref="IInstrumentationSourceProvider"/>. Emission is a no-op while no listener or
/// exporter is attached, so instrumentation can be left in place unconditionally.
/// </summary>
/// <remarks>
/// Every name this interface takes - the source's own, an activity's, an instrument's - is a static
/// one the code decides, never a value from a request. An instrument is cached for the life of the
/// process and a metric name becomes a series in whatever backend is collecting, so a name built from
/// a tenant, a user or an entity id grows both without bound. Request data belongs on the returned
/// <see cref="Activity"/> as a tag, and only where the deployment has decided that tag may carry it.
/// </remarks>
public interface IInstrumentationSource
{
    /// <summary>Gets the source name, used as both the activity source and meter name.</summary>
    string Name { get; }

    /// <summary>
    /// Starts an activity (span) when a listener is sampling this source; otherwise returns
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="name">The operation name for the activity. A static one, never request data.</param>
    /// <param name="kind">The activity kind. Defaults to <see cref="ActivityKind.Internal"/>.</param>
    /// <returns>The started activity, or <see langword="null"/> when nobody is listening.</returns>
    Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal);

    /// <summary>
    /// Gets the counter instrument with the given name on this source's meter, creating it once
    /// and caching it for subsequent calls.
    /// </summary>
    /// <param name="name">The instrument name. A static one: it is a metric series, not a value.</param>
    /// <param name="unit">An optional unit of measure (e.g. "s", "By").</param>
    /// <returns>The cached counter instrument.</returns>
    Counter<long> GetOrCreateCounter(string name, string? unit = null);

    /// <summary>
    /// Gets the histogram instrument with the given name on this source's meter, creating it once
    /// and caching it for subsequent calls.
    /// </summary>
    /// <param name="name">The instrument name. A static one: it is a metric series, not a value.</param>
    /// <param name="unit">An optional unit of measure (e.g. "s", "By").</param>
    /// <returns>The cached histogram instrument.</returns>
    Histogram<double> GetOrCreateHistogram(string name, string? unit = null);
}
