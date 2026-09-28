using Bsft.Hosting.Cors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering BSFT CORS services.
/// </summary>
public static class CorsServiceCollectionExtensions
{
    /// <summary>
    /// Adds CORS services with restrictive defaults (no origins allowed).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftCorsOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftCors(
        this IServiceCollection services,
        Action<BsftCorsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Adds CORS services, bound from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Hosting:Cors</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddBsftCors(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftCorsOptions>? configure = null)
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
        IServiceCollection services, IConfiguration? configuration, Action<BsftCorsOptions>? configure)
    {
        void Apply(BsftCorsOptions target)
        {
            configuration?.GetSection(BsftCorsOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        var options = new BsftCorsOptions();
        Apply(options);

        services.AddOptions<BsftCorsOptions>().Configure(Apply).ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftCorsOptions>, BsftCorsOptionsValidator>());

        services.AddCors(corsOptions =>
        {
            corsOptions.AddPolicy(HostingDefaults.CorsPolicyName, builder =>
            {
                if (options.IsAnyOriginAllowed)
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyMethod()
                           .AllowAnyHeader();
                    return;
                }

                if (options.Origins.Count > 0)
                {
                    builder.WithOrigins([.. options.Origins]);
                }

                builder.AllowAnyMethod()
                       .AllowAnyHeader();

                if (options.AreCredentialsAllowed)
                {
                    builder.AllowCredentials();
                }
            });
        });

        return services;
    }
}
