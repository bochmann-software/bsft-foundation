namespace Bsft.Data.Postgres;

/// <summary>
/// Maps two-letter language codes to the PostgreSQL text-search configurations used for stemming and
/// stopword handling.
/// </summary>
/// <remarks>
/// Internal on purpose. The mapping is how this provider talks to PostgreSQL, not something a caller
/// declares against: a search group names a language code and the provider decides what that means.
/// Unlike SQLite, PostgreSQL ships a real Snowball stemmer per language, so <c>de</c> here is German
/// stemming and not an approximation of it.
/// </remarks>
/// <seealso cref="BsftDataPostgresOptions.UseTextSearchConfiguration"/>
internal static class FullTextLanguageResolver
{
    private static readonly Dictionary<string, string> _configurations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = "german",
        ["en"] = "english",
        ["fr"] = "french",
        ["it"] = "italian",
        ["es"] = "spanish",
        ["nl"] = "dutch",
    };

    private static readonly string[] _supportedCodes = [.. _configurations.Keys.OrderBy(c => c, StringComparer.Ordinal)];

    /// <summary>
    /// Gets the language codes this mapping knows, in a stable order so an error message naming them
    /// reads the same on every machine.
    /// </summary>
    public static IReadOnlyList<string> SupportedCodes => _supportedCodes;

    /// <summary>
    /// Resolves the text-search configuration for a language code.
    /// </summary>
    /// <param name="language">A two-letter language code such as <c>de</c>, or <c>null</c>.</param>
    /// <returns>
    /// The configuration name, or <c>null</c> when the code is unknown or absent. A null result means
    /// the query is issued against the server's <c>default_text_search_config</c>, which is preferable
    /// to guessing wrong and stemming German text with English rules. The fallback stays silent by
    /// design and is not silent in effect: startup validation refuses a group whose language lands
    /// here, and the migration helper refuses to emit DDL for one, so a deployment reaches the server
    /// default only by choosing to.
    /// </returns>
    public static string? Resolve(string? language) =>
        language is not null && _configurations.TryGetValue(language, out var configuration) ? configuration : null;
}
