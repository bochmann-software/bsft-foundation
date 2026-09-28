using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Bsft.Logging;

/// <summary>
/// Configuration options for the BSFT logging infrastructure.
/// </summary>
public sealed class BsftLoggingOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Logging";

    /// <summary>The largest file size a host may configure, past which rolling is meaningless.</summary>
    private const int MaxFileSizeLimitMb = 1024;

    /// <summary>
    /// The longest retention a host may configure. Ten years is past every retention a log file is
    /// kept under, and the bound is what stops the cleanup's cutoff from being a date that cannot be
    /// represented — which would fail the provider's construction, and with it the host's start.
    /// </summary>
    private const int MaxRetainDaysLimit = 3650;

    /// <summary>
    /// Whether to write logs to files. Default: true.
    /// </summary>
    public bool EnableFileLogging { get; set; } = true;

    /// <summary>
    /// Directory for log files. A relative path is resolved from the process working directory.
    /// Default: "logs".
    /// </summary>
    public string LogDirectory { get; set; } = "logs";

    /// <summary>
    /// Maximum size per log file in MB before rolling to a new file. Default: 10.
    /// </summary>
    public int MaxFileSizeMb { get; set; } = 10;

    /// <summary>
    /// Maximum UTF-8 bytes retained for one physical line or multiline entry while reading.
    /// Default: 1 MiB.
    /// </summary>
    public int MaxEntryBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Number of days to retain log files. Older files are deleted on startup. Default: 30.
    /// </summary>
    public int RetainDays { get; set; } = 30;

    /// <summary>
    /// When true, uses JSON output format (one JSON object per line). Default: false (structured text lines).
    /// </summary>
    public bool EnableJsonFormat { get; set; }

    /// <summary>
    /// Minimum log level for the file logger. Default: <see cref="LogLevel.Information"/>.
    /// </summary>
    /// <remarks>
    /// The file logger's level, not the pipeline's: raising it quietens the file and leaves the
    /// console where it was. Lowering it below <see cref="LogLevel.Information"/> does reach the
    /// console, because a record the logging factory drops is never offered to any provider, so the
    /// pipeline has to carry what the file asks for.
    /// </remarks>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>How often a live log-file tail checks for newly appended entries.</summary>
    public TimeSpan LogReaderPollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Throws when a configured value cannot be used, naming the member and the value.</summary>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used.</exception>
    /// <remarks>
    /// Only values the provider cannot work with are rejected, and only while file logging is on: a
    /// host that turned the file sink off is not asked to supply a directory it has no use for.
    /// </remarks>
    internal void Validate()
    {
        ValidateReaderRules();

        if (!EnableFileLogging)
            return;

        ValidateFileRules();
    }

    /// <summary>Throws when a value the file sink needs cannot be used, naming the member and the value.</summary>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used.</exception>
    /// <remarks>
    /// Checked without asking whether file logging is on, because constructing
    /// <see cref="BsftFileLoggerProvider"/> is itself the decision to write to a file: a host that
    /// goes straight to the provider never passes the flag this class reads.
    /// </remarks>
    internal void ValidateFileRules()
    {
        if (string.IsNullOrWhiteSpace(LogDirectory))
            throw Invalid(nameof(LogDirectory), "must name a directory", LogDirectory ?? "<null>");

        if (MaxFileSizeMb is < 1 or > MaxFileSizeLimitMb)
        {
            throw Invalid(
                nameof(MaxFileSizeMb),
                FormattableString.Invariant($"must be between 1 and {MaxFileSizeLimitMb}"),
                MaxFileSizeMb);
        }

        if (RetainDays is < 1 or > MaxRetainDaysLimit)
        {
            throw Invalid(
                nameof(RetainDays),
                FormattableString.Invariant($"must be between 1 and {MaxRetainDaysLimit} days"),
                RetainDays);
        }
    }

    /// <summary>Throws when the file-reader polling interval or entry byte limit cannot be used.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the polling interval or <see cref="MaxEntryBytes"/> is not positive.
    /// </exception>
    internal void ValidateReaderRules()
    {
        if (LogReaderPollInterval <= TimeSpan.Zero)
            throw Invalid(nameof(LogReaderPollInterval), "must be positive", LogReaderPollInterval);

        if (MaxEntryBytes <= 0)
            throw Invalid(nameof(MaxEntryBytes), "must be positive", MaxEntryBytes);
    }

    /// <summary>Builds the one exception shape every failure above takes.</summary>
    /// <param name="member">The member that cannot be used.</param>
    /// <param name="problem">What is wrong with it, as a clause completing "must …".</param>
    /// <param name="value">The offending value, named so the message is actionable.</param>
    /// <returns>The exception to throw.</returns>
    private static InvalidOperationException Invalid(string member, string problem, object value)
        => new(string.Create(
            CultureInfo.InvariantCulture,
            $"{nameof(BsftLoggingOptions)}.{member} {problem}, but it is {value}."));
}
