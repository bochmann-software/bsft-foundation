using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Instrumentation.DependencyInjection;

/// <summary>
/// Extension methods for registering vendor-neutral instrumentation sources.
/// </summary>
public static class InstrumentationServiceCollectionExtensions
{
    /// <summary>
    /// Registers a named instrumentation source and advertises it for observability collection.
    /// Idempotently ensures the meter factory and <see cref="IInstrumentationSourceProvider"/> are
    /// available. The advertised <see cref="ObservabilitySourceRegistration"/> is discovered by an
    /// observability backend (e.g. Bsft.Observability) without it referencing this caller.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The source name (used as activity source and meter name).</param>
    /// <param name="signals">
    /// The signals the source is advertised for. Defaults to <see cref="ObservabilitySignals.All"/>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBsftInstrumentationSource(
        this IServiceCollection services, string name, ObservabilitySignals signals = ObservabilitySignals.All)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(ObservabilitySourceRegistration.NameRequiredMessage, nameof(name));

        services.AddMetrics();
        services.TryAddSingleton<IInstrumentationSourceProvider, InstrumentationSourceProvider>();

        // Register each distinct source once so the collector's discovery is free of duplicates.
        var alreadyRegistered = services.Any(d =>
            d.ServiceType == typeof(ObservabilitySourceRegistration)
            && d.ImplementationInstance is ObservabilitySourceRegistration existing
            && string.Equals(existing.Name, name, StringComparison.Ordinal));

        if (!alreadyRegistered)
            services.AddSingleton(new ObservabilitySourceRegistration(
                name,
                signals.HasFlag(ObservabilitySignals.Traces),
                signals.HasFlag(ObservabilitySignals.Metrics)));

        return services;
    }
}
