namespace Bsft.Instrumentation;

/// <summary>
/// Advertises a telemetry source (by name) that an observability backend should collect.
/// Modules and consumers register this in the service collection so the collector can discover
/// the source without a compile-time reference to the emitting package.
/// </summary>
/// <param name="Name">The <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/> name to collect.</param>
/// <param name="EnableTracing">Whether the named activity source should be collected as traces.</param>
/// <param name="EnableMetrics">Whether the named meter should be collected as metrics.</param>
public sealed record ObservabilitySourceRegistration(string Name, bool EnableTracing = true, bool EnableMetrics = true)
{
    /// <summary>Shared message for an absent instrumentation source name.</summary>
    internal const string NameRequiredMessage = "Instrumentation source name must be provided.";

    /// <summary>Gets the activity source and meter name to collect.</summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(Name)
        ? throw new ArgumentException(NameRequiredMessage, nameof(Name))
        : Name;
}
