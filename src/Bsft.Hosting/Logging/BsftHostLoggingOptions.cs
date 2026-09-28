using Microsoft.Extensions.Logging;

namespace Bsft.Hosting.Logging;

/// <summary>
/// Host-level logging policy: the per-category minimum levels and which log providers the host
/// switches on. File logging itself is owned by <c>Bsft.Logging</c> and configured through
/// <see cref="FileLogging"/>.
/// </summary>
public sealed class BsftHostLoggingOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    /// <remarks>
    /// Deliberately not <c>Bsft:Logging</c>: that section belongs to
    /// <see cref="Bsft.Logging.BsftLoggingOptions"/>, which describes the file provider. A host that calls
    /// both entry points binds two different shapes, so they must not share a section.
    /// </remarks>
    public const string SectionName = "Bsft:Hosting:Logging";

    /// <summary>
    /// Default log level for application code. Default: <see cref="LogLevel.Information"/>.
    /// </summary>
    public LogLevel DefaultLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Log level for Microsoft.* namespaces. Default: <see cref="LogLevel.Warning"/>.
    /// </summary>
    public LogLevel MicrosoftLevel { get; set; } = LogLevel.Warning;

    /// <summary>
    /// Log level for System.* namespaces. Default: <see cref="LogLevel.Warning"/>.
    /// </summary>
    public LogLevel SystemLevel { get; set; } = LogLevel.Warning;

    /// <summary>
    /// Log level for Microsoft.AspNetCore.* namespaces. Default: <see cref="LogLevel.Warning"/>.
    /// </summary>
    public LogLevel AspNetCoreLevel { get; set; } = LogLevel.Warning;

    /// <summary>
    /// When true, the console provider writes JSON instead of the BSFT text format. Default: false.
    /// This affects the console only; the file format is controlled by
    /// <see cref="Bsft.Logging.BsftLoggingOptions.EnableJsonFormat"/>.
    /// </summary>
    public bool UseJsonFormat { get; set; }

    /// <summary>
    /// When true, includes scopes in console output (needed for CorrelationId). Default: true.
    /// </summary>
    public bool IncludeScopes { get; set; } = true;

    /// <summary>
    /// When true, enables the console log provider. Default: true.
    /// </summary>
    public bool EnableConsole { get; set; } = true;

    /// <summary>
    /// When true, enables the debug log provider. Default: true.
    /// </summary>
    public bool EnableDebug { get; set; } = true;

    /// <summary>
    /// The configuration applied to the file logger options, or <see langword="null"/> when the
    /// caller did not customize file logging.
    /// </summary>
    /// <remarks>
    /// A delegate, so no configuration provider can supply it: file logging is configured in code,
    /// through <see cref="FileLogging"/>, and the <c>Bsft:Hosting:Logging</c> section binds every other
    /// member of this type but not this one.
    /// </remarks>
    internal Action<Bsft.Logging.BsftLoggingOptions>? FileConfigure { get; private set; }

    /// <summary>
    /// Configures the <c>Bsft.Logging</c> file logger used by the host, including whether it is
    /// enabled at all (<see cref="Bsft.Logging.BsftLoggingOptions.EnableFileLogging"/>), the log
    /// directory, the rolling size and the retention window.
    /// </summary>
    /// <param name="configure">An action to configure <see cref="Bsft.Logging.BsftLoggingOptions"/>.</param>
    /// <returns>The current options instance for fluent chaining.</returns>
    public BsftHostLoggingOptions FileLogging(Action<Bsft.Logging.BsftLoggingOptions> configure)
    {
        FileConfigure = configure ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

    /// <summary>
    /// Builds the file logger options for this host configuration. The most permissive of the
    /// configured category levels seeds the file logger's minimum level before
    /// <see cref="FileLogging"/> runs, so the file provider never filters out an entry that the
    /// host's filter pipeline admitted for some category — the file keeps seeing what the console
    /// sees. An explicit <see cref="Bsft.Logging.BsftLoggingOptions.MinimumLevel"/> still wins, for
    /// a file that is deliberately quieter than the rest.
    /// </summary>
    /// <returns>The configured file logger options.</returns>
    internal Bsft.Logging.BsftLoggingOptions BuildFileOptions()
    {
        var fileOptions = new Bsft.Logging.BsftLoggingOptions { MinimumLevel = MostPermissiveLevel() };
        FileConfigure?.Invoke(fileOptions);
        return fileOptions;
    }

    /// <summary>
    /// The lowest (most verbose) of the configured category levels. A per-category level looser
    /// than <see cref="DefaultLevel"/> lets that category through the host's filter pipeline, so
    /// the file provider's own gate has to be at least that permissive to stay in step.
    /// </summary>
    /// <returns>The most permissive configured level.</returns>
    private LogLevel MostPermissiveLevel()
    {
        var level = DefaultLevel;

        if (MicrosoftLevel < level)
            level = MicrosoftLevel;

        if (SystemLevel < level)
            level = SystemLevel;

        if (AspNetCoreLevel < level)
            level = AspNetCoreLevel;

        return level;
    }
}
