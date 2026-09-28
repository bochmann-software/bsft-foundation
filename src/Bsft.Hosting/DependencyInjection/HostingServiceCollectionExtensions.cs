using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering all BSFT hosting defaults as a single convenience call.
/// </summary>
public static class HostingServiceCollectionExtensions
{
    /// <summary>
    /// Registers all BSFT hosting components with sensible defaults.
    /// Individual components can be customized through the <paramref name="configure"/> action.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftDefaultsConfiguration"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftDefaults(
        this IServiceCollection services,
        Action<BsftDefaultsConfiguration>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Registers all BSFT hosting components, binding each component's options from its own
    /// configuration section before the <paramref name="configure"/> action adjusts them.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// The configuration carrying the <c>Bsft:Hosting:*</c> sections. Each component binds its own
    /// section — a component whose section is absent keeps its defaults.
    /// </param>
    /// <param name="configure">An optional action to configure <see cref="BsftDefaultsConfiguration"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddBsftDefaults(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftDefaultsConfiguration>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return Add(services, configuration, configure);
    }

    /// <summary>Registers every component, whichever overload was called.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration to bind each section from, or <see langword="null"/>.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftDefaultsConfiguration"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Each component keeps its own options type and its own section rather than becoming a nested group
    /// on one hosting options type, because a host may call <c>AddBsftCors</c> on its own without ever
    /// calling this method. This call is a convenience over the individual ones, not the owner of their
    /// configuration.
    /// </remarks>
    private static IServiceCollection Add(
        IServiceCollection services, IConfiguration? configuration, Action<BsftDefaultsConfiguration>? configure)
    {
        var config = new BsftDefaultsConfiguration();
        configure?.Invoke(config);

        // Fully qualified where Microsoft.Extensions.DependencyInjection ships a class of the same
        // simple name; both are in scope here and neither name is ours to change.
        Bsft.Hosting.DependencyInjection.HostLoggingServiceCollectionExtensions.Add(services, configuration, config.LoggingConfigure);
        Bsft.Hosting.DependencyInjection.SwaggerServiceCollectionExtensions.Add(services, configuration, config.SwaggerConfigure);
        VersioningServiceCollectionExtensions.Add(services, configuration, config.VersioningConfigure);
        Bsft.Hosting.DependencyInjection.CorsServiceCollectionExtensions.Add(services, configuration, config.CorsConfigure);
        RateLimitingServiceCollectionExtensions.Add(services, configuration, config.RateLimitingConfigure);
        CompressionServiceCollectionExtensions.Add(services, configuration, config.CompressionConfigure);
        JsonServiceCollectionExtensions.Add(services, configuration, config.JsonConfigure);
        FilteringServiceCollectionExtensions.Add(services, configuration, config.FilterTransportConfigure);
        HttpsServiceCollectionExtensions.Add(services, configuration, config.ApplyHttps);
        services.AddBsftHealthChecks();
        services.AddBsftStartupValidation();

        return services;
    }
}
