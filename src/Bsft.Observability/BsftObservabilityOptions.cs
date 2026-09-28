using Bsft.Correlation;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Bsft.Observability;

/// <summary>
/// Configures how <c>AddBsftObservability</c> wires OpenTelemetry traces and metrics.
/// Unset OTLP values fall back to the standard <c>OTEL_*</c> environment variables.
/// </summary>
public sealed class BsftObservabilityOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Observability";

    /// <summary>Gets or sets the resource service name. When null, the SDK default (or OTEL_SERVICE_NAME) applies.</summary>
    public string? ServiceName { get; set; }

    /// <summary>Gets or sets the resource service version.</summary>
    public string? ServiceVersion { get; set; }

    /// <summary>Gets or sets a value indicating whether trace collection is enabled.</summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether metric collection is enabled.</summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether OTLP log export is enabled. Disabled by default: unlike
    /// tracing and metrics, this attaches a provider to the application's own <c>ILoggingBuilder</c>
    /// pipeline, which is a bigger behavioral change to opt a consumer into implicitly.
    /// </summary>
    public bool EnableLogging { get; set; }

    /// <summary>Gets or sets a value indicating whether ASP.NET Core instrumentation is enabled.</summary>
    public bool EnableAspNetCoreInstrumentation { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether outgoing HttpClient instrumentation is enabled.</summary>
    public bool EnableHttpClientInstrumentation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether EF Core instrumentation is enabled. Disabled by default
    /// because the instrumentation package is pre-release.
    /// </summary>
    public bool EnableEntityFrameworkCoreInstrumentation { get; set; }

    /// <summary>Gets or sets a value indicating whether process runtime metrics (GC, threads) are collected.</summary>
    public bool EnableRuntimeMetrics { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the OTLP exporter is added (the default exporter).</summary>
    public bool EnableOtlpExporter { get; set; } = true;

    /// <summary>Gets or sets the OTLP endpoint. When null, the SDK default (or OTEL_EXPORTER_OTLP_ENDPOINT) applies.</summary>
    public Uri? OtlpEndpoint { get; set; }

    /// <summary>Gets or sets the OTLP protocol. When null, the SDK default applies.</summary>
    public OtlpExportProtocol? OtlpProtocol { get; set; }

    /// <summary>Gets or sets a value indicating whether the console exporter is added (useful in development).</summary>
    public bool EnableConsoleExporter { get; set; }

    /// <summary>Gets or sets the request header read to enrich spans with the correlation ID.</summary>
    public string CorrelationHeaderName { get; set; } = CorrelationDefaults.CorrelationId;

    /// <summary>Gets additional activity source names to collect (beyond the discovered Bsft sources).</summary>
    public IList<string> AdditionalActivitySources { get; } = [];

    /// <summary>Gets additional meter names to collect (beyond the discovered Bsft sources).</summary>
    public IList<string> AdditionalMeters { get; } = [];

    /// <summary>Gets or sets a hook to further configure the tracer provider, invoked after the framework defaults.</summary>
    public Action<TracerProviderBuilder>? ConfigureTracerProvider { get; set; }

    /// <summary>Gets or sets a hook to further configure the meter provider, invoked after the framework defaults.</summary>
    public Action<MeterProviderBuilder>? ConfigureMeterProvider { get; set; }

    /// <summary>Gets or sets a hook to further configure the logger provider, invoked after the framework defaults.</summary>
    public Action<LoggerProviderBuilder>? ConfigureLoggerProvider { get; set; }

    /// <summary>Throws when a configured value cannot be used, naming the member and the value.</summary>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used.</exception>
    /// <remarks>
    /// A null <see cref="ServiceName"/> is accepted and means "let the SDK decide", which is what
    /// <c>OTEL_SERVICE_NAME</c> is for. A blank one is not the same thing: it names a resource nobody
    /// can find in a backend, and it is the shape a configuration key set to an empty string takes.
    /// </remarks>
    internal void Validate()
    {
        if (ServiceName is not null && string.IsNullOrWhiteSpace(ServiceName))
            throw Invalid(nameof(ServiceName), "must not be blank when it is provided at all");

        if (string.IsNullOrWhiteSpace(CorrelationHeaderName))
            throw Invalid(nameof(CorrelationHeaderName), "must name a request header");
    }

    /// <summary>Builds the one exception shape every failure above takes.</summary>
    /// <param name="member">The member that cannot be used.</param>
    /// <param name="problem">What is wrong with it, as a clause completing "must …".</param>
    /// <returns>The exception to throw.</returns>
    private static InvalidOperationException Invalid(string member, string problem)
        => new($"{nameof(BsftObservabilityOptions)}.{member} {problem}.");
}
