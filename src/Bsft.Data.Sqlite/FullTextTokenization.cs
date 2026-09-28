namespace Bsft.Data.Sqlite;

/// <summary>
/// Answers whether an FTS5 index built with a given tokenizer stems a given language.
/// </summary>
/// <remarks>
/// Internal on purpose, exactly as its SQL Server counterpart: the mapping is how this provider talks
/// to the engine, not something a caller declares against. The answer is nearly always "no". FTS5
/// ships one stemmer, Porter, and Porter is English. That is a property of the engine and not a gap
/// in this package, which is why the provider reports it at startup instead of quietly indexing
/// German text unstemmed.
/// </remarks>
internal static class FullTextTokenization
{
    /// <summary>The one language FTS5 can stem, and only with the Porter tokenizer.</summary>
    private const string StemmedLanguage = "en";

    /// <summary>FTS5 defines <c>remove_diacritics</c> as 0, 1 or 2 and rejects anything else.</summary>
    private const int MaxRemoveDiacritics = 2;

    /// <summary>
    /// Gets the language codes an FTS5 index can stem, in a stable order so an error message naming
    /// them reads the same on every machine.
    /// </summary>
    public static IReadOnlyList<string> StemmedCodes { get; } = [StemmedLanguage];

    /// <summary>
    /// Whether an index built with <paramref name="tokenizer"/> stems <paramref name="language"/>.
    /// </summary>
    /// <param name="language">A two-letter language code such as <c>de</c>, or <c>null</c>.</param>
    /// <param name="tokenizer">The tokenizer the index is built with.</param>
    /// <returns><see langword="true"/> only for English on the Porter tokenizer.</returns>
    public static bool CanStem(string? language, SqliteTokenizer tokenizer) =>
        tokenizer is SqliteTokenizer.Porter
        && string.Equals(language, StemmedLanguage, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the <c>tokenize=</c> argument of the <c>CREATE VIRTUAL TABLE</c> statement.
    /// </summary>
    /// <param name="tokenizer">The tokenizer to use.</param>
    /// <param name="removeDiacritics">The FTS5 <c>remove_diacritics</c> level.</param>
    /// <returns>The FTS5 tokenizer specification.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the tokenizer or the folding level is not a declared value.</exception>
    /// <remarks>
    /// The framework's default is <c>remove_diacritics 2</c> rather than the FTS5 default of 1: the
    /// engine's default folds only the Latin-1 range, which leaves half of the diacritics an
    /// application will actually meet unfolded. Without folding, a search for <c>muller</c> does not
    /// find <c>müller</c> — the exact surprise this package exists to remove. The <c>ascii</c>
    /// tokenizer takes no such argument at all: it treats everything above U+007F as a token
    /// character, so there is nothing for it to fold and the level is left off its specification.
    /// </remarks>
    public static string Describe(SqliteTokenizer tokenizer, int removeDiacritics)
    {
        var folding = RequireRemoveDiacritics(removeDiacritics, nameof(removeDiacritics));

        return tokenizer switch
        {
            SqliteTokenizer.Unicode61 => $"unicode61 remove_diacritics {folding}",
            SqliteTokenizer.Porter => $"porter unicode61 remove_diacritics {folding}",
            SqliteTokenizer.Ascii => "ascii",
            _ => throw new ArgumentOutOfRangeException(nameof(tokenizer), tokenizer, "Unknown FTS5 tokenizer."),
        };
    }

    /// <summary>
    /// Checks a diacritic folding level against what FTS5 accepts.
    /// </summary>
    /// <param name="value">The level to check.</param>
    /// <param name="parameterName">The parameter to blame when it is not one.</param>
    /// <returns>The level itself, so a caller can assign the checked value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the level is not 0, 1 or 2.</exception>
    /// <remarks>
    /// Checked here rather than left to the engine: an out-of-range level makes
    /// <c>CREATE VIRTUAL TABLE</c> fail during a migration, which is a deployment that stops halfway
    /// rather than an application that refuses to start.
    /// </remarks>
    public static int RequireRemoveDiacritics(int value, string parameterName)
    {
        if (value is < 0 or > MaxRemoveDiacritics)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"FTS5 accepts remove_diacritics 0, 1 or {MaxRemoveDiacritics}.");
        }

        return value;
    }
}
