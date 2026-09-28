using Asp.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering BSFT API versioning services.
/// </summary>
public static class VersioningServiceCollectionExtensions
{
    /// <summary>
    /// Scale used to turn the fractional part of a decimal version (1.1) into its minor component (1).
    /// </summary>
    private const int MinorVersionScale = 10;

    /// <summary>
    /// Adds API versioning with URL segment versioning (/api/v1/...).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftVersioningOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftApiVersioning(
        this IServiceCollection services,
        Action<BsftVersioningOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Adds API versioning with URL segment versioning, bound from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Hosting:Versioning</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddBsftApiVersioning(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftVersioningOptions>? configure = null)
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
        IServiceCollection services, IConfiguration? configuration, Action<BsftVersioningOptions>? configure)
    {
        void Apply(BsftVersioningOptions target)
        {
            configuration?.GetSection(BsftVersioningOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        var options = new BsftVersioningOptions();
        Apply(options);

        services.AddOptions<BsftVersioningOptions>().Configure(Apply).ValidateDataAnnotations().ValidateOnStart();

        var majorVersion = (int)options.DefaultVersion;
        var minorVersion = (int)((options.DefaultVersion - majorVersion) * MinorVersionScale);

        services.AddApiVersioning(o =>
        {
            o.DefaultApiVersion = new ApiVersion(majorVersion, minorVersion);
            o.AssumeDefaultVersionWhenUnspecified = options.ShouldAssumeDefaultVersionWhenUnspecified;
            o.ReportApiVersions = options.ShouldReportApiVersions;
            o.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddApiExplorer(o =>
        {
            o.GroupNameFormat = "'v'VVV";
            o.SubstituteApiVersionInUrl = true;
        });

        return services;
    }
}
