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
    string? SearchText = null)
{
    /// <summary>
    /// Gets an optional exact, case-sensitive correlation id. An entry matches when its
    /// <see cref="LogFileEntry.CorrelationId"/>, the innermost <c>CorrelationId</c> scope active when it was
    /// logged, equals this value. Null, empty or whitespace applies no correlation filter, and an entry logged
    /// outside any scope has no id, so no value selects it.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets an optional exact, case-sensitive scope filter: every pair must equal the entry's
    /// <see cref="LogFileEntry.ScopeProperties"/> value for that key (AND semantics). A null or empty filter
    /// applies none. <see cref="CorrelationId"/> is matched the same way, as the <c>CorrelationId</c> pair.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ScopeFilter { get; init; }
}
