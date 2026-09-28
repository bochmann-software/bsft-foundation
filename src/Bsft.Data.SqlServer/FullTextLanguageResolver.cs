namespace Bsft.Data.SqlServer;

/// <summary>
/// Maps two-letter language codes to the SQL Server locale identifiers used for stemming and
/// stopword handling.
/// </summary>
/// <remarks>
/// Internal on purpose. The mapping is how this provider talks to SQL Server, not something a caller
/// declares against: a search group names a language code and the provider decides what that means.
/// </remarks>
internal static class FullTextLanguageResolver
{
    private static readonly Dictionary<string, int> _lcids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = 1031,
        ["en"] = 1033,
        ["fr"] = 1036,
        ["it"] = 1040,
        ["es"] = 3082,
        ["nl"] = 1043,
    };

    private static readonly string[] _supportedCodes = [.. _lcids.Keys.OrderBy(c => c, StringComparer.Ordinal)];

    /// <summary>
    /// Gets the language codes this mapping knows, in a stable order so an error message naming them
    /// reads the same on every machine.
    /// </summary>
    public static IReadOnlyList<string> SupportedCodes => _supportedCodes;

    /// <summary>
    /// Resolves the locale identifier for a language code.
    /// </summary>
    /// <param name="language">A two-letter language code such as <c>de</c>, or <c>null</c>.</param>
    /// <returns>
    /// The locale identifier, or <c>null</c> when the code is unknown or absent. A null result means the query
    /// is issued without an explicit language, letting the server fall back to the index default —
    /// which is preferable to guessing wrong and stemming German text with English rules.
    /// </returns>
    public static int? Resolve(string? language) =>
        language is not null && _lcids.TryGetValue(language, out var lcid) ? lcid : null;
}
