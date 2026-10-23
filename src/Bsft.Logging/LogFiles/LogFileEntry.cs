using Bsft.Correlation;
using Microsoft.Extensions.Logging;

namespace Bsft.Logging.LogFiles;

/// <summary>Represents one parsed entry from a BSFT rolling log file.</summary>
/// <param name="Sequence">Stable sequence derived from file date, roll index and byte offset.</param>
/// <param name="Timestamp">The recorded local timestamp, when parsing succeeded.</param>
/// <param name="Level">The recorded log level, or <see cref="LogLevel.None"/> for malformed input.</param>
/// <param name="CorrelationId">The correlation id, or null when the entry was logged outside any correlation scope or is malformed.</param>
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
    // The map a producer supplied, which a copy keeps, and the map derived from the correlation id on first
    // read, which a copy never takes over: it would otherwise describe the id of the entry it was copied from.
    private IReadOnlyDictionary<string, string>? _suppliedScopeProperties;
    private IReadOnlyDictionary<string, string>? _derivedScopeProperties;

    /// <summary>Copies an entry for a <c>with</c> expression, leaving the derived scope map to be built again.</summary>
    /// <param name="original">The entry being copied.</param>
    private LogFileEntry(LogFileEntry original)
    {
        Sequence = original.Sequence;
        Timestamp = original.Timestamp;
        Level = original.Level;
        CorrelationId = original.CorrelationId;
        Category = original.Category;
        Message = original.Message;
        Exception = original.Exception;
        RawText = original.RawText;
        IsMalformed = original.IsMalformed;
        IsTruncated = original.IsTruncated;
        IsScopeTruncated = original.IsScopeTruncated;
        _suppliedScopeProperties = original._suppliedScopeProperties;
    }

    /// <summary>Gets whether input beyond the configured entry byte limit was omitted.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>
    /// Gets whether the writer dropped scope properties of this line because they did not fit its budget, so a
    /// property that is absent from <see cref="ScopeProperties"/> may have been in scope.
    /// </summary>
    public bool IsScopeTruncated { get; init; }

    /// <summary>
    /// Gets the scope values the file logger persisted on the line, by key: the correlation id under
    /// <see cref="LogScopeKeys.CorrelationId"/> when the line has one, plus every other configured key that was
    /// in scope. Empty when there are none, for a line written before scope properties existed, and for malformed input.
    /// </summary>
    /// <remarks>
    /// A line with a correlation id and nothing else builds the map when it is first read, so a scan that never
    /// looks at it allocates none. A copy made with <c>with</c> does not depend on whether the original was read.
    /// </remarks>
    public IReadOnlyDictionary<string, string> ScopeProperties
    {
        get => _suppliedScopeProperties ?? (_derivedScopeProperties ??= CorrelationOnly(CorrelationId));
        init => _suppliedScopeProperties = value;
    }

    /// <summary>Gets whether the map has been built or supplied, which a line without extra properties defers.</summary>
    internal bool AreScopePropertiesMaterialized => _suppliedScopeProperties is not null || _derivedScopeProperties is not null;

    /// <summary>The shared empty map, so an entry without scope values allocates none.</summary>
    internal static IReadOnlyDictionary<string, string> NoScopeProperties { get; } = new Dictionary<string, string>().AsReadOnly();

    /// <summary>Reports whether a scope value of the entry equals one exactly (ordinal), without building a deferred map.</summary>
    /// <param name="key">The scope key.</param>
    /// <param name="value">The wanted value.</param>
    /// <returns><see langword="true"/> when the entry carries the key with exactly that value.</returns>
    internal bool HasScopeValue(string key, string value)
    {
        if (_suppliedScopeProperties is null)
        {
            return CorrelationId is not null
                && string.Equals(key, LogScopeKeys.CorrelationId, StringComparison.Ordinal)
                && string.Equals(CorrelationId, value, StringComparison.Ordinal);
        }

        return _suppliedScopeProperties.TryGetValue(key, out var actual) && string.Equals(actual, value, StringComparison.Ordinal);
    }

    /// <summary>Compares every member by value, the scope properties by their content.</summary>
    /// <param name="other">The entry to compare with.</param>
    /// <returns><see langword="true"/> when both entries state the same.</returns>
    public bool Equals(LogFileEntry? other)
        => other is not null
            && Sequence == other.Sequence
            && Timestamp == other.Timestamp
            && Level == other.Level
            && CorrelationId == other.CorrelationId
            && Category == other.Category
            && Message == other.Message
            && Exception == other.Exception
            && RawText == other.RawText
            && IsMalformed == other.IsMalformed
            && IsTruncated == other.IsTruncated
            && IsScopeTruncated == other.IsScopeTruncated
            && AreSameScopeProperties(ScopeProperties, other.ScopeProperties);

    /// <summary>Hashes the members that <see cref="Equals(LogFileEntry?)"/> compares, the scope properties by their content.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Sequence);
        hash.Add(Timestamp);
        hash.Add(Level);
        hash.Add(CorrelationId);
        hash.Add(Category);
        hash.Add(Message);
        hash.Add(Exception);
        hash.Add(RawText);
        hash.Add(IsMalformed);
        hash.Add(IsTruncated);
        hash.Add(IsScopeTruncated);

        // Order-independent, as the comparison is.
        var properties = 0;
        foreach (var pair in ScopeProperties)
            properties ^= HashCode.Combine(pair.Key, pair.Value);

        hash.Add(properties);
        return hash.ToHashCode();
    }

    private static IReadOnlyDictionary<string, string> CorrelationOnly(string? correlationId)
        => correlationId is null
            ? NoScopeProperties
            : new Dictionary<string, string> { [LogScopeKeys.CorrelationId] = correlationId }.AsReadOnly();

    // Ordinal on both sides whatever comparer a supplied map was built with, so that a.Equals(b) and b.Equals(a) agree.
    private static bool AreSameScopeProperties(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        if (ReferenceEquals(left, right))
            return true;

        return left.Count == right.Count && left.All(pair => ContainsOrdinal(right, pair));
    }

    private static bool ContainsOrdinal(IReadOnlyDictionary<string, string> map, KeyValuePair<string, string> pair)
        => map.Any(candidate => string.Equals(candidate.Key, pair.Key, StringComparison.Ordinal)
            && string.Equals(candidate.Value, pair.Value, StringComparison.Ordinal));
}
