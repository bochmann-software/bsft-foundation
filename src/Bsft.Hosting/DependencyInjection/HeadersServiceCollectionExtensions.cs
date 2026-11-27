using Bsft.Hosting.SecurityHeaders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>Extension methods for registering the BSFT security headers. Named <c>Headers*</c> rather than <c>SecurityHeaders*</c>: the 40-character type-name rule leaves no room, and <c>Bsft.Security</c> already has a <c>SecurityServiceCollectionExtensions</c>.</summary>
public static class HeadersServiceCollectionExtensions
{
    /// <summary>Registers and validates <see cref="BsftSecurityHeadersOptions"/> with its secure defaults.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure the options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftSecurityHeaders(
        this IServiceCollection services,
        Action<BsftSecurityHeadersOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>Registers and validates <see cref="BsftSecurityHeadersOptions"/>, bound from configuration.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Hosting:SecurityHeaders</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddBsftSecurityHeaders(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftSecurityHeadersOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return Add(services, configuration, configure);
    }

    private static IServiceCollection Add(
        IServiceCollection services, IConfiguration? configuration, Action<BsftSecurityHeadersOptions>? configure)
    {
        services.AddOptions<BsftSecurityHeadersOptions>()
            .Configure(target =>
            {
                configuration?.GetSection(BsftSecurityHeadersOptions.SectionName).Bind(target);
                configure?.Invoke(target);
            })
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftSecurityHeadersOptions>, BsftSecurityHeadersOptionsValidator>());

        return services;
    }
}
