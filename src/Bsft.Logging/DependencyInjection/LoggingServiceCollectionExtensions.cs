using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bsft.Logging.DependencyInjection;

/// <summary>
/// Extension methods for registering BSFT logging services on <see cref="IServiceCollection"/>.
/// </summary>
public static class LoggingServiceCollectionExtensions
{
    /// <summary>The level the pipeline carries when no sink asks for more.</summary>
    private const LogLevel DefaultPipelineLevel = LogLevel.Information;

    /// <summary>
    /// Adds the BSFT logging infrastructure with sensible defaults including structured console output,
    /// file logging with daily rotation, and appropriate log level filters for Microsoft.* and System.* namespaces.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftLoggingOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    public static IServiceCollection AddBsftLogging(
        this IServiceCollection services,
        Action<BsftLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Adds the BSFT logging infrastructure, bound from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Logging</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    public static IServiceCollection AddBsftLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftLoggingOptions>? configure = null)
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
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    /// <remarks>
    /// The logging builder is configured from an instance built here rather than from
    /// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>, because filters and providers are decided while the
    /// collection is still being built and there is no provider to resolve from yet. The same values
    /// are registered through the options pattern so that a consumer can read them, and they are
    /// validated on this call as well as on start.
    /// </remarks>
    private static IServiceCollection Add(
        IServiceCollection services, IConfiguration? configuration, Action<BsftLoggingOptions>? configure)
    {
        void Apply(BsftLoggingOptions target)
        {
            configuration?.GetSection(BsftLoggingOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        var options = new BsftLoggingOptions();

        Apply(options);
        options.Validate();

        LoggingBuilderExtensions.AddBsftFileServices(services, Apply);

        services.AddLogging(builder =>
        {
            builder.ClearProviders();

            // The pipeline's floor is the most verbose level any sink asks for, not the file sink's
            // own level. The file sink states that level again in BsftFileLogger.IsEnabled, so a host
            // quietening the file quietens the file alone — but asking the file for more than the
            // floor has to lower the floor, because the factory drops a record before any provider is
            // offered it, and the console then sees those records too.
            builder.SetMinimumLevel(PipelineLevelFor(options));
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();

            if (options.EnableFileLogging)
                LoggingBuilderExtensions.AddBsftFileLoggerProvider(builder, Apply);
        });

        return services;
    }

    /// <summary>Reads the level the whole pipeline has to carry for every sink to be served.</summary>
    /// <param name="options">The configured options.</param>
    /// <returns>The floor to set on the builder.</returns>
    private static LogLevel PipelineLevelFor(BsftLoggingOptions options)
        => options.EnableFileLogging && options.MinimumLevel < DefaultPipelineLevel
            ? options.MinimumLevel
            : DefaultPipelineLevel;
}
