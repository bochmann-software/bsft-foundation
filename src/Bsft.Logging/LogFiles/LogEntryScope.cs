using System.Text.Json;
using Bsft.Correlation;

namespace Bsft.Logging.LogFiles;

/// <summary>Builds the scope property map of a decoded entry from the pieces the two line formats carry.</summary>
internal static class LogEntryScope
{
    /// <summary>Reads the string members of the <c>scope</c> object, which only lines written with extra keys have.</summary>
    public static List<KeyValuePair<string, string>>? FromJson(JsonElement root)
    {
        if (!root.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object)
            return null;

        var properties = new List<KeyValuePair<string, string>>();
        foreach (var member in scope.EnumerateObject())
        {
            if (member.Value.ValueKind == JsonValueKind.String)
                properties.Add(new KeyValuePair<string, string>(member.Name, member.Value.GetString() ?? string.Empty));
        }

        return properties;
    }

    /// <summary>Adds the extra scope properties a line carried to the entry decoded from it.</summary>
    /// <remarks>
    /// The correlation id comes from the line's own slot. A <c>CorrelationId</c> key among the extras is ignored, so
    /// a hand-written line cannot give an entry a correlation id its slot does not state. The truncation marker is
    /// lifted into <see cref="LogFileEntry.IsScopeTruncated"/>. An entry with nothing left over keeps the lazy map.
    /// </remarks>
    /// <param name="entry">The decoded entry.</param>
    /// <param name="extras">The properties of the block or <c>scope</c> object, or null when the line has none.</param>
    /// <returns>The entry, with its scope properties when the line had any beyond the correlation id.</returns>
    internal static LogFileEntry WithScope(LogFileEntry entry, List<KeyValuePair<string, string>>? extras)
    {
        if (extras is null || extras.Count == 0)
            return entry;

        var truncated = false;
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (entry.CorrelationId is not null)
            properties[LogScopeKeys.CorrelationId] = entry.CorrelationId;

        foreach (var pair in extras)
        {
            if (string.Equals(pair.Key, ScopePropertyEncoding.TruncatedKey, StringComparison.Ordinal))
                truncated |= string.Equals(pair.Value, ScopePropertyEncoding.TruncatedValue, StringComparison.Ordinal);
            else if (!string.Equals(pair.Key, LogScopeKeys.CorrelationId, StringComparison.Ordinal))
                properties.TryAdd(pair.Key, pair.Value);
        }

        if (properties.Count == (entry.CorrelationId is null ? 0 : 1))
            return truncated ? entry with { IsScopeTruncated = true } : entry;

        return entry with { ScopeProperties = properties.AsReadOnly(), IsScopeTruncated = truncated };
    }
}
