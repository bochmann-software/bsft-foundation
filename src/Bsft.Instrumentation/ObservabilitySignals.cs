namespace Bsft.Instrumentation;

/// <summary>
/// The observability signals an instrumentation source is advertised for. A collector reads the
/// advertised signals to decide whether the source contributes traces, metrics, or both.
/// </summary>
[Flags]
public enum ObservabilitySignals
{
    /// <summary>The source is registered but not collected.</summary>
    None = 0,

    /// <summary>The source is collected as traces.</summary>
    Traces = 1,

    /// <summary>The source is collected as metrics.</summary>
    Metrics = 2,

    /// <summary>The source is collected as both traces and metrics.</summary>
    All = Traces | Metrics
}
