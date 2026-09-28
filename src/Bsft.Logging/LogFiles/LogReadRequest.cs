using Microsoft.Extensions.Logging;

namespace Bsft.Logging.LogFiles;

/// <summary>Describes one newest-first page read from the BSFT log files.</summary>
/// <param name="PageSize">Maximum number of entries to return.</param>
/// <param name="Before">Exclusive sequence of the oldest entry already returned.</param>
/// <param name="MinimumLevel">Optional inclusive minimum level.</param>
/// <param name="IncludedLevels">Optional exact set of included levels.</param>
/// <param name="SearchText">Optional case-insensitive text matched over category, message, and exception.</param>
public sealed record LogReadRequest(
    int PageSize = LogFileReaderDefaults.PageSize,
    long? Before = null,
    LogLevel? MinimumLevel = null,
    IReadOnlySet<LogLevel>? IncludedLevels = null,
    string? SearchText = null);
