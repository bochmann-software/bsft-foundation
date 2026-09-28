using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering BSFT response compression services.
/// </summary>
public static class CompressionServiceCollectionExtensions
{
    /// <summary>
    /// Adds response compression with Gzip as the default provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftCompressionOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftCompression(
        this IServiceCollection services,
        Action<BsftCompressionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Adds response compression, bound from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Hosting:Compression</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddBsftCompression(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftCompressionOptions>? configure = null)
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
        IServiceCollection services, IConfiguration? configuration, Action<BsftCompressionOptions>? configure)
    {
        void Apply(BsftCompressionOptions target)
        {
            configuration?.GetSection(BsftCompressionOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        var options = new BsftCompressionOptions();
        Apply(options);

        services.AddOptions<BsftCompressionOptions>().Configure(Apply).ValidateDataAnnotations().ValidateOnStart();

        services.AddResponseCompression(compressionOptions =>
        {
            compressionOptions.EnableForHttps = options.EnableForHttps;

            compressionOptions.Providers.Add<GzipCompressionProvider>();

            if (options.EnableBrotli)
            {
                compressionOptions.Providers.Add<BrotliCompressionProvider>();
            }
        });

        return services;
    }
}
