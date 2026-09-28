using Bsft.Logging.LogFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Logging.DependencyInjection;

/// <summary>
/// Extension methods for registering the BSFT file logger on <see cref="ILoggingBuilder"/>.
/// </summary>
public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Adds the BSFT file logger provider to the logging pipeline. This enables writing log entries
    /// to daily-rotated files with optional size-based rolling and automatic cleanup of old files.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftLoggingOptions"/>.</param>
    /// <returns>The logging builder for chaining.</returns>
    public static ILoggingBuilder AddBsftFileLogger(
        this ILoggingBuilder builder,
        Action<BsftLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        AddBsftFileServices(builder.Services, configure);
        return AddBsftFileLoggerProvider(builder, configure);
    }

    /// <summary>Registers the file reader and its shared dependencies for either public entry point.</summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="configure">Applies the file options used by the reader.</param>
    internal static void AddBsftFileServices(
        IServiceCollection services,
        Action<BsftLoggingOptions>? configure)
    {
        services.AddOptions<BsftLoggingOptions>().Configure(configure ?? (_ => { })).ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftLoggingOptions>, BsftLoggingOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ILogFileAccess, LogFileAccess>();
        services.TryAddSingleton<ILogFileNaming, LogFileNaming>();
        services.TryAddSingleton<ILogFileReader>(provider => new LogFileReader(
            provider.GetRequiredService<IOptions<BsftLoggingOptions>>().Value,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogFileAccess>(),
            provider.GetRequiredService<ILogFileNaming>()));
    }

    /// <summary>Registers only the writer after shared file services have already been added.</summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="configure">Applies the writer options.</param>
    /// <returns>The logging builder for chaining.</returns>
    internal static ILoggingBuilder AddBsftFileLoggerProvider(
        ILoggingBuilder builder,
        Action<BsftLoggingOptions>? configure)
    {
        var options = new BsftLoggingOptions();
        configure?.Invoke(options);

        // Registered through a factory rather than as a pre-built instance: the container disposes
        // only what it created itself, and the file provider drains its queue in Dispose. A
        // pre-built instance would therefore never be drained, losing whatever was still queued at
        // shutdown — in practice the shutdown-phase log lines, because the background flush thread
        // is a background thread and dies with the process.
        builder.Services.AddSingleton<ILoggerProvider>(provider => new BsftFileLoggerProvider(
            options,
            provider.GetService<TimeProvider>(),
            provider.GetRequiredService<ILogFileNaming>()));

        return builder;
    }
}
