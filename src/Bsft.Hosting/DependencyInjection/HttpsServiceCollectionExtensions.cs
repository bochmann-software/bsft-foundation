using Bsft.Hosting.Https;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering BSFT HTTPS/HSTS defaults.
/// </summary>
public static class HttpsServiceCollectionExtensions
{
    /// <summary>
    /// The HSTS max-age advertised to browsers. One year is the value required for HSTS preload.
    /// </summary>
    private static readonly TimeSpan HstsMaxAge = TimeSpan.FromDays(365);

    /// <summary>
    /// The port HTTP requests are redirected to: the default HTTPS port.
    /// </summary>
    private const int DefaultHttpsPort = 443;

    /// <summary>
    /// Adds HTTPS redirection and HSTS services with sensible defaults.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftHttpsOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftHttpsDefaults(
        this IServiceCollection services,
        Action<BsftHttpsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Adds HTTPS redirection and HSTS services, bound from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Hosting:Https</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddBsftHttpsDefaults(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftHttpsOptions>? configure = null)
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
    internal static IServiceCollection Add(
        IServiceCollection services, IConfiguration? configuration, Action<BsftHttpsOptions>? configure)
    {
        void Apply(BsftHttpsOptions target)
        {
            configuration?.GetSection(BsftHttpsOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        services.AddOptions<BsftHttpsOptions>().Configure(Apply).ValidateDataAnnotations().ValidateOnStart();

        services.AddHsts(options =>
        {
            options.MaxAge = HstsMaxAge;
            options.IncludeSubDomains = true;
            options.Preload = true;
        });

        services.AddHttpsRedirection(options =>
        {
            options.HttpsPort = DefaultHttpsPort;
        });

        return services;
    }
}
