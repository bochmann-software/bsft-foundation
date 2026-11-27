using System.Globalization;
using Bsft.Correlation;
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

    private const int DefaultMaxFileSizeMb = 10;
    private const int DefaultMaxEntryBytes = 1024 * 1024;
    private const int DefaultRetainDays = 30;
    private const int DefaultMaxScopePropertyChars = 1024;
    private const int DefaultPollIntervalMs = 250;

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
    public int MaxFileSizeMb { get; set; } = DefaultMaxFileSizeMb;

    /// <summary>
    /// Maximum UTF-8 bytes retained for one physical line or multiline entry while reading.
    /// Default: 1 MiB.
    /// </summary>
    public int MaxEntryBytes { get; set; } = DefaultMaxEntryBytes;

    /// <summary>
    /// Number of days to retain log files. Older files are deleted on startup and on each day rollover. Default: 30.
    /// </summary>
    public int RetainDays { get; set; } = DefaultRetainDays;

    /// <summary>
    /// Maximum number of log files kept, the one being written included. When more exist, the oldest are
    /// deleted at start-up and on every roll-over. <see langword="null"/> or 0 keeps every file. Default: unlimited.
    /// </summary>
    /// <remarks>
    /// Assumes one writer per log directory, see <see cref="MaxTotalSizeMb"/>.
    /// </remarks>
    public int? MaxRetainedFiles { get; set; }

    /// <summary>
    /// Maximum combined size of all log files in MB, the one being written included. When it is exceeded the
    /// oldest files are deleted at start-up and on every roll-over; the file being written is never deleted.
    /// <see langword="null"/> or 0 sets no size limit. When set it must be at least twice
    /// <see cref="MaxFileSizeMb"/>: a rolled file is at least that large, so the limit keeps roughly
    /// <c>MaxTotalSizeMb / MaxFileSizeMb - 1</c> full rolled files besides the one being written.
    /// Default: unlimited.
    /// </summary>
    /// <remarks>
    /// This limit and <see cref="MaxRetainedFiles"/> assume one writer per log directory: a provider that
    /// deletes a file another provider or process is still appending to loses that data without an error on
    /// Linux. Hosts that share a directory should rely on <see cref="RetainDays"/> alone.
    /// </remarks>
    public int? MaxTotalSizeMb { get; set; }

    /// <summary>
    /// When true, uses JSON output format (one JSON object per line). Default: false (structured text lines).
    /// </summary>
    public bool EnableJsonFormat { get; set; }

    /// <summary>
    /// Gets or sets the scope keys the file logger persists on every line, in the order they are written. The
    /// innermost value of each key active when the line is logged is stored. The default is
    /// <see cref="LogScopeKeys.CorrelationId"/> alone, which is written to the line's dedicated correlation slot;
    /// every other key is written as a scope property map. Defaults to the correlation id only.
    /// </summary>
    public IList<string> PersistedScopeKeys { get; set; } = new List<string> { LogScopeKeys.CorrelationId };

    /// <summary>
    /// Gets or sets how many characters of keys and values the scope property map of one line may hold in total,
    /// the correlation id excepted. Beyond it the last value is cut and the remaining properties are left out.
    /// Defaults to 1024.
    /// </summary>
    public int MaxScopePropertyChars { get; set; } = DefaultMaxScopePropertyChars;

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
    public TimeSpan LogReaderPollInterval { get; set; } = TimeSpan.FromMilliseconds(DefaultPollIntervalMs);

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

        if (ScopePropertyEncoding.TryFindKeyProblem(PersistedScopeKeys, out var problem, out var problemValue))
            throw Invalid(nameof(PersistedScopeKeys), problem, problemValue);

        if (MaxScopePropertyChars <= 0)
            throw Invalid(nameof(MaxScopePropertyChars), "must be positive", MaxScopePropertyChars);

        if (RetainDays is < 1 or > MaxRetainDaysLimit)
        {
            throw Invalid(
                nameof(RetainDays),
                FormattableString.Invariant($"must be between 1 and {MaxRetainDaysLimit} days"),
                RetainDays);
        }

        ValidateFileLimits();
    }

    /// <summary>Throws when the file-count or total-size limit is negative or leaves no room for one file.</summary>
    /// <exception cref="InvalidOperationException">Thrown when a limit cannot be used.</exception>
    private void ValidateFileLimits()
    {
        if (MaxRetainedFiles < 0)
            throw Invalid(nameof(MaxRetainedFiles), "must not be negative", MaxRetainedFiles.Value);

        if (MaxTotalSizeMb < 0)
            throw Invalid(nameof(MaxTotalSizeMb), "must not be negative", MaxTotalSizeMb.Value);

        if (MaxTotalSizeMb is > 0 && MaxTotalSizeMb < 2 * MaxFileSizeMb)
        {
            throw Invalid(
                nameof(MaxTotalSizeMb),
                FormattableString.Invariant($"must be at least twice {nameof(MaxFileSizeMb)} ({2 * MaxFileSizeMb} MB), one rolled file plus the one being written, or the rolled file is deleted at once; 0 means no limit"),
                MaxTotalSizeMb.Value);
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
