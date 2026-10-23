using Bsft.Hosting.Logging;
using Bsft.Logging;
using Bsft.Logging.Sinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering BSFT logging services.
/// </summary>
public static class HostLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Adds the BSFT logging infrastructure with sensible defaults including structured text output,
    /// correlation ID support, and appropriate log level filters. File logging is provided by the
    /// <c>Bsft.Logging</c> package and configured through
    /// <see cref="BsftHostLoggingOptions.FileLogging"/>.
    /// </summary>
    /// <remarks>
    /// Named <c>AddBsftHostLogging</c> rather than <c>AddBsftLogging</c> so it cannot become
    /// ambiguous with <c>AddBsftLogging</c> on
    /// <see cref="Bsft.Logging.DependencyInjection.LoggingServiceCollectionExtensions"/> in a
    /// file that imports both namespaces.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftHostLoggingOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftHostLogging(
        this IServiceCollection services,
        Action<BsftHostLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Adds the BSFT logging infrastructure with its policy bound from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Hosting:Logging</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <remarks>
    /// The section is <c>Bsft:Hosting:Logging</c> and not <c>Bsft:Logging</c>, which belongs to
    /// <c>Bsft.Logging.BsftLoggingOptions</c>. The two describe different things — a host's category
    /// levels and providers here, the file provider's directory, rolling and retention there — and a
    /// host that calls both entry points would otherwise bind two shapes from one section, each silently
    /// dropping the keys it does not know. File logging stays configured in code through
    /// <see cref="BsftHostLoggingOptions.FileLogging"/>.
    /// </remarks>
    public static IServiceCollection AddBsftHostLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftHostLoggingOptions>? configure = null)
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
        IServiceCollection services, IConfiguration? configuration, Action<BsftHostLoggingOptions>? configure)
    {
        void Apply(BsftHostLoggingOptions target)
        {
            configuration?.GetSection(BsftHostLoggingOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        var options = new BsftHostLoggingOptions();
        Apply(options);

        services.AddOptions<BsftHostLoggingOptions>().Configure(Apply).ValidateDataAnnotations().ValidateOnStart();

        var fileOptions = options.BuildFileOptions();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();

            builder.SetMinimumLevel(options.DefaultLevel);
            builder.AddFilter("Microsoft", options.MicrosoftLevel);
            builder.AddFilter("System", options.SystemLevel);
            builder.AddFilter("Microsoft.AspNetCore", options.AspNetCoreLevel);

            AddConsoleProvider(builder, options, fileOptions);

            if (options.EnableDebug)
            {
                builder.AddDebug();
            }

            if (fileOptions.EnableFileLogging)
            {
                // Registered through a factory rather than as a pre-built instance: the container
                // disposes only what it created itself, and the file provider drains its queue in
                // Dispose. A pre-built instance would therefore never be drained, losing whatever
                // was still queued at shutdown — in practice the shutdown-phase log lines.
                builder.Services.AddSingleton<ILoggerProvider>(_ => new BsftFileLoggerProvider(fileOptions));
            }
        });

        return services;
    }

    private static void AddConsoleProvider(ILoggingBuilder builder, BsftHostLoggingOptions options, Bsft.Logging.BsftLoggingOptions fileOptions)
    {
        if (!options.EnableConsole)
            return;

        if (options.UseJsonFormat)
        {
            builder.AddJsonConsole(jsonOptions =>
            {
                jsonOptions.IncludeScopes = options.IncludeScopes;
            });

            return;
        }

        builder.AddConsole(consoleOptions =>
        {
            consoleOptions.FormatterName = HostingDefaults.ConsoleFormatterName;
        });

        builder.Services.TryAddSingleton<IScopeLineEncoder, ScopeLineEncoder>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftConsoleFormatterOptions>, BsftConsoleFormatterOptionsValidator>());
        builder.AddConsoleFormatter<BsftConsoleFormatter, BsftConsoleFormatterOptions>(formatterOptions =>
        {
            formatterOptions.IncludeScopes = options.IncludeScopes;
            formatterOptions.PersistedScopeKeys = [.. fileOptions.PersistedScopeKeys];
        });
    }
}
