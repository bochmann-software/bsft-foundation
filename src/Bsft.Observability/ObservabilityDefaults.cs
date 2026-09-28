namespace Bsft.Observability;

/// <summary>The names this package puts on the wire, for a host that has to query by them.</summary>
public static class ObservabilityDefaults
{
    /// <summary>
    /// The span tag carrying the correlation id read off the incoming request.
    /// </summary>
    /// <remarks>
    /// A trace backend filters by the tag's name rather than by the header's, so the two are
    /// different constants: the header a deployment may rename through
    /// <see cref="BsftObservabilityOptions.CorrelationHeaderName"/>, the tag is what every dashboard
    /// and saved query written against this package names, and it stays put.
    /// </remarks>
    public const string CorrelationTagName = "correlation_id";
}
