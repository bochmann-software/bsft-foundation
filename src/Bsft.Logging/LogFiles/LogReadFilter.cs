using Bsft.Correlation;

namespace Bsft.Logging.LogFiles;

/// <summary>Decides whether an entry satisfies the filters of a read request.</summary>
internal static class LogReadFilter
{
    internal static bool IsMatch(LogFileEntry entry, LogReadRequest query)
    {
        if (query.MinimumLevel is { } minimum && entry.Level < minimum)
            return false;

        if (query.IncludedLevels is { } included && !included.Contains(entry.Level))
            return false;

        if (!string.IsNullOrWhiteSpace(query.CorrelationId)
            && !entry.HasScopeValue(LogScopeKeys.CorrelationId, query.CorrelationId))
        {
            return false;
        }

        if (query.ScopeFilter is { Count: > 0 } filter)
        {
            foreach (var pair in filter)
            {
                if (!entry.HasScopeValue(pair.Key, pair.Value))
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(query.SearchText))
            return true;

        return Contains(entry.Category, query.SearchText)
            || Contains(entry.Message, query.SearchText)
            || Contains(entry.Exception, query.SearchText);
    }

    private static bool Contains(string? value, string searchText)
        => value?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true;
}
