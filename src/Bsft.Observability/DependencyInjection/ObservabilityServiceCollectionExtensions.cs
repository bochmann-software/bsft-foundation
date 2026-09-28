using System.Diagnostics;
using System.Text.RegularExpressions;
using Bsft.Instrumentation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Bsft.Observability.DependencyInjection;

/// <summary>
/// Extension methods that wire OpenTelemetry traces and metrics for BSFT applications and discover
/// the instrumentation sources advertised via <see cref="ObservabilitySourceRegistration"/>.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    // Mirrors CorrelationIdMiddleware: only well-formed IDs are recorded so unvalidated client input
    // never reaches telemetry. The end anchor is \z rather than $ for the same reason the check is
    // there: $ also matches just before a single trailing newline, which is precisely the character a
    // tag must not carry into a line-based sink.
    private static readonly Regex SafeCorrelationId =
        new(@"^[A-Za-z0-9._-]{1,128}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Adds OpenTelemetry traces and metrics configured by <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftObservabilityOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member.</exception>
    public static IServiceCollection AddBsftObservability(
        this IServiceCollection services, Action<BsftObservabilityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Adds OpenTelemetry traces and metrics with options bound from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Observability</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member.</exception>
    /// <remarks>
    /// This takes the configuration root and reads
    /// <see cref="BsftObservabilityOptions.SectionName"/> from it, rather than taking the section
    /// itself. Every entry point in the framework binds the same way, so a host never has to remember
    /// which of the two a particular package wanted.
    /// </remarks>
    public static IServiceCollection AddBsftObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftObservabilityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return Add(services, configuration, configure);
    }

    /// <summary>Binds, validates and registers, whichever overload was called.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration to bind from, or <see langword="null"/>.</param>
    /// <param name="configure">Adjusts what the section bound.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member.</exception>
    /// <remarks>
    /// The OpenTelemetry pipelines are configured from an instance built here rather than from
    /// <see cref="IOptions{TOptions}"/>, because the tracer and meter builders are assembled while the
    /// collection is still being built and there is no provider to resolve from yet. The same values
    /// are registered through the options pattern so a consumer can read them, and they are validated
    /// on this call as well as on start.
    /// </remarks>
    private static IServiceCollection Add(
        IServiceCollection services, IConfiguration? configuration, Action<BsftObservabilityOptions>? configure)
    {
        void Apply(BsftObservabilityOptions target)
        {
            configuration?.GetSection(BsftObservabilityOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        var options = new BsftObservabilityOptions();

        Apply(options);
        options.Validate();

        services.AddOptions<BsftObservabilityOptions>().Configure(Apply).ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftObservabilityOptions>, BsftObservabilityOptionsValidator>());

        var builder = services.AddOpenTelemetry();

        builder.ConfigureResource(resource =>
        {
            if (!string.IsNullOrWhiteSpace(options.ServiceName))
                resource.AddService(options.ServiceName, serviceVersion: options.ServiceVersion);
        });

        if (options.EnableTracing)
            ConfigureTracing(services, builder, options);

        if (options.EnableMetrics)
            ConfigureMetrics(services, builder, options);

        if (options.EnableLogging)
            ConfigureLogging(builder, options);

        return services;
    }

    private static void ConfigureTracing(IServiceCollection services, IOpenTelemetryBuilder builder, BsftObservabilityOptions options)
    {
        builder.WithTracing(tracing => ConfigureTracingPipeline(tracing, options));

        // Discovery runs at provider-build time, so it sees every source regardless of whether the
        // module was registered before or after AddBsftObservability.
        services.ConfigureOpenTelemetryTracerProvider((provider, tracing) => AddDiscoveredSources(provider, tracing, options));
    }

    private static void ConfigureTracingPipeline(TracerProviderBuilder tracing, BsftObservabilityOptions options)
    {
        if (options.EnableAspNetCoreInstrumentation)
            tracing.AddAspNetCoreInstrumentation(instrumentation => instrumentation.EnrichWithHttpRequest = CorrelationEnricher(options.CorrelationHeaderName));

        if (options.EnableHttpClientInstrumentation)
            tracing.AddHttpClientInstrumentation();

        if (options.EnableEntityFrameworkCoreInstrumentation)
            tracing.AddEntityFrameworkCoreInstrumentation();

        if (options.EnableOtlpExporter)
            tracing.AddOtlpExporter(exporter => ApplyOtlp(exporter, options));

        if (options.EnableConsoleExporter)
            tracing.AddConsoleExporter();

        options.ConfigureTracerProvider?.Invoke(tracing);
    }

    private static void AddDiscoveredSources(IServiceProvider provider, TracerProviderBuilder tracing, BsftObservabilityOptions options)
    {
        foreach (var registration in provider.GetServices<ObservabilitySourceRegistration>())
        {
            if (registration.EnableTracing)
                tracing.AddSource(registration.Name);
        }

        foreach (var source in options.AdditionalActivitySources)
            tracing.AddSource(source);
    }

    private static void ConfigureMetrics(IServiceCollection services, IOpenTelemetryBuilder builder, BsftObservabilityOptions options)
    {
        builder.WithMetrics(metrics => ConfigureMetricsPipeline(metrics, options));

        services.ConfigureOpenTelemetryMeterProvider((provider, metrics) => AddDiscoveredMeters(provider, metrics, options));
    }

    private static void ConfigureMetricsPipeline(MeterProviderBuilder metrics, BsftObservabilityOptions options)
    {
        if (options.EnableAspNetCoreInstrumentation)
            metrics.AddAspNetCoreInstrumentation();

        if (options.EnableHttpClientInstrumentation)
            metrics.AddHttpClientInstrumentation();

        if (options.EnableRuntimeMetrics)
            metrics.AddRuntimeInstrumentation();

        if (options.EnableOtlpExporter)
            metrics.AddOtlpExporter(exporter => ApplyOtlp(exporter, options));

        if (options.EnableConsoleExporter)
            metrics.AddConsoleExporter();

        options.ConfigureMeterProvider?.Invoke(metrics);
    }

    private static void AddDiscoveredMeters(IServiceProvider provider, MeterProviderBuilder metrics, BsftObservabilityOptions options)
    {
        foreach (var registration in provider.GetServices<ObservabilitySourceRegistration>())
        {
            if (registration.EnableMetrics)
                metrics.AddMeter(registration.Name);
        }

        foreach (var meter in options.AdditionalMeters)
            metrics.AddMeter(meter);
    }

    private static void ConfigureLogging(IOpenTelemetryBuilder builder, BsftObservabilityOptions options)
    {
        // Unlike tracing and metrics, there is nothing to discover here: ILogger categories are not
        // advertised sources, every category is collected unless the host filters it.
        builder.WithLogging(
            logging => ConfigureLoggingPipeline(logging, options),
            loggerOptions =>
            {
                loggerOptions.IncludeFormattedMessage = true;
                loggerOptions.IncludeScopes = true;
            });
    }

    private static void ConfigureLoggingPipeline(LoggerProviderBuilder logging, BsftObservabilityOptions options)
    {
        if (options.EnableOtlpExporter)
            logging.AddOtlpExporter(exporter => ApplyOtlp(exporter, options));

        if (options.EnableConsoleExporter)
            logging.AddConsoleExporter();

        options.ConfigureLoggerProvider?.Invoke(logging);
    }

    private static Action<Activity, HttpRequest> CorrelationEnricher(string headerName) =>
        (activity, request) => EnrichWithCorrelationId(activity, request, headerName);

    internal static void ApplyOtlp(OpenTelemetry.Exporter.OtlpExporterOptions exporter, BsftObservabilityOptions options)
    {
        if (options.OtlpEndpoint is not null)
            exporter.Endpoint = options.OtlpEndpoint;
        if (options.OtlpProtocol is not null)
            exporter.Protocol = options.OtlpProtocol.Value;
    }

    internal static void EnrichWithCorrelationId(Activity activity, HttpRequest request, string headerName)
    {
        // Implicit StringValues -> string? conversion avoids the LINQ enumerator allocation per request.
        string? value = request.Headers[headerName];
        if (!string.IsNullOrEmpty(value) && SafeCorrelationId.IsMatch(value))
            activity.SetTag(ObservabilityDefaults.CorrelationTagName, value);
    }
}
