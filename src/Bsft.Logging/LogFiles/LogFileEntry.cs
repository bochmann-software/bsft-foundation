using Microsoft.Extensions.Logging;

namespace Bsft.Logging.LogFiles;

/// <summary>Represents one parsed entry from a BSFT rolling log file.</summary>
/// <param name="Sequence">Stable sequence derived from file date, roll index and byte offset.</param>
/// <param name="Timestamp">The recorded local timestamp, when parsing succeeded.</param>
/// <param name="Level">The recorded log level, or <see cref="LogLevel.None"/> for malformed input.</param>
/// <param name="CorrelationId">The correlation id, when present.</param>
/// <param name="Category">The logger category, when present.</param>
/// <param name="Message">The log message, or the unparsed line for malformed input.</param>
/// <param name="Exception">The multiline or JSON exception, when present.</param>
/// <param name="RawText">The complete source text represented by this entry.</param>
/// <param name="IsMalformed">Whether the source could not be parsed as a BSFT text or JSON entry.</param>
public sealed record LogFileEntry(
    long Sequence,
    DateTimeOffset? Timestamp,
    LogLevel Level,
    string? CorrelationId,
    string? Category,
    string Message,
    string? Exception,
    string RawText,
    bool IsMalformed)
{
    /// <summary>Gets whether input beyond the configured entry byte limit was omitted.</summary>
    public bool IsTruncated { get; init; }
}
