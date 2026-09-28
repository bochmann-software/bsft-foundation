namespace Bsft.Data.Sqlite;

/// <summary>
/// Declares which search groups are backed by an FTS5 index, which table holds it, and how the
/// application answers for the languages FTS5 cannot stem.
/// </summary>
/// <remarks>
/// <para>
/// The framework cannot inspect the schema at startup without a connection, so the application
/// states what it has created. A group that is declared searchable but not listed here fails startup
/// validation — the deliberate alternative to silently scanning the table.
/// </para>
/// <para>
/// This is the package's primary options type and it deliberately carries no <c>SectionName</c> and
/// binds from no configuration section. Every member of it describes the <em>schema</em>: the
/// tokenizer and the folding level are compiled into the FTS5 table when the migration creates it, so
/// changing one means rebuilding the index rather than restarting the process. A deployment that
/// could state them separately could state them differently, and the provider would then certify
/// stemming for an index that has none — the quiet degradation this package exists to prevent. The
/// declaration is therefore written beside the migration that makes it true.
/// </para>
/// </remarks>
public sealed class BsftDataSqliteOptions
{
    /// <summary>The diacritic folding level assumed when the application does not choose one.</summary>
    public const int DefaultRemoveDiacritics = 2;

    private readonly Dictionary<string, string> _indexedGroups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unstemmedLanguages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the declared groups mapped to the FTS5 table backing each one.
    /// </summary>
    public IReadOnlyDictionary<string, string> IndexedGroups => _indexedGroups;

    /// <summary>
    /// Gets the tokenizer the indexes are built with. It decides both how text is split and whether
    /// anything is stemmed, so it is stated once for every index this application creates.
    /// </summary>
    public SqliteTokenizer Tokenizer { get; private set; } = SqliteTokenizer.Unicode61;

    /// <summary>
    /// Gets the FTS5 <c>remove_diacritics</c> level the indexes are built with, deciding how far
    /// accents are folded away before terms are stored.
    /// </summary>
    /// <remarks>
    /// It is part of the index, not of the query: changing it means rebuilding, because the terms
    /// already stored were folded under the old level.
    /// </remarks>
    public int RemoveDiacritics { get; private set; } = DefaultRemoveDiacritics;

    /// <summary>
    /// Declares that an FTS5 index exists for the given search group.
    /// </summary>
    /// <param name="groupName">The search group name.</param>
    /// <param name="indexTable">
    /// The FTS5 table backing the group. Unlike SQL Server, where the index hangs off the table it
    /// indexes, an FTS5 index <em>is</em> a table of its own and has to be named.
    /// </param>
    /// <returns>The same options instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when an argument is null or whitespace.</exception>
    public BsftDataSqliteOptions HasFullTextIndex(string groupName, string indexTable)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            throw new ArgumentException("A search group name is required.", nameof(groupName));
        if (string.IsNullOrWhiteSpace(indexTable))
            throw new ArgumentException("An FTS5 table name is required.", nameof(indexTable));

        _indexedGroups[groupName] = indexTable;
        return this;
    }

    /// <summary>
    /// Sets the tokenizer the indexes are built with.
    /// </summary>
    /// <param name="tokenizer">The tokenizer.</param>
    /// <returns>The same options instance for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the tokenizer is not a declared value.</exception>
    public BsftDataSqliteOptions UseTokenizer(SqliteTokenizer tokenizer)
    {
        // Validated here rather than at first use, so a bad value fails while the application is
        // being wired up instead of on the first search.
        _ = FullTextTokenization.Describe(tokenizer, RemoveDiacritics);

        Tokenizer = tokenizer;
        return this;
    }

    /// <summary>
    /// Sets how far the indexes fold diacritics away.
    /// </summary>
    /// <param name="removeDiacritics">
    /// The FTS5 level: <c>0</c> folds nothing, <c>1</c> folds the Latin-1 range only, <c>2</c> folds
    /// every diacritic Unicode defines one for.
    /// </param>
    /// <returns>The same options instance for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the level is not 0, 1 or 2.</exception>
    /// <remarks>
    /// The framework's default of <c>2</c> is what makes <c>muller</c> find <c>müller</c>, so lowering
    /// it is a deliberate trade rather than a tuning knob: an index built with <c>0</c> keeps accents
    /// apart, which a deployment sometimes needs and a user searching from a plain keyboard rarely
    /// wants. The <c>ascii</c> tokenizer ignores this entirely — it has no diacritics to fold.
    /// </remarks>
    public BsftDataSqliteOptions UseRemoveDiacritics(int removeDiacritics)
    {
        RemoveDiacritics = FullTextTokenization.RequireRemoveDiacritics(removeDiacritics, nameof(removeDiacritics));
        return this;
    }

    /// <summary>
    /// Accepts that a declared language is searched without stemming.
    /// </summary>
    /// <remarks>
    /// FTS5 has no German stemmer, so a group declaring <c>de</c> gets exact-token matching:
    /// <c>schweisser</c> does not find <c>schweissern</c>. That is a usable search and a perfectly
    /// reasonable choice — but it is not what the declaration promises, and a search that quietly
    /// answers a narrower question than it was asked is precisely what this feature exists to
    /// prevent. So the trade-off is accepted out loud here, or startup fails.
    /// </remarks>
    /// <param name="language">The language code searched without stemming.</param>
    /// <returns>The same options instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="language"/> is null or whitespace.</exception>
    public BsftDataSqliteOptions AcceptsUnstemmedLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("A language code is required.", nameof(language));

        _unstemmedLanguages.Add(language);
        return this;
    }

    /// <summary>
    /// Copies this declaration onto the instance the container will hand to the provider.
    /// </summary>
    /// <param name="target">The instance the options pattern is building.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is null.</exception>
    /// <remarks>
    /// The folding level is copied before the tokenizer, because <see cref="UseTokenizer"/> checks the
    /// pair and would otherwise judge the new tokenizer against the default level rather than this
    /// declaration's own.
    /// </remarks>
    internal void CopyTo(BsftDataSqliteOptions target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.UseRemoveDiacritics(RemoveDiacritics);
        target.UseTokenizer(Tokenizer);

        foreach (var language in _unstemmedLanguages)
            target.AcceptsUnstemmedLanguage(language);

        foreach (var (groupName, indexTable) in _indexedGroups)
            target.HasFullTextIndex(groupName, indexTable);
    }

    /// <summary>Gets the FTS5 table backing a group, or <see langword="null"/> when it is not declared.</summary>
    /// <param name="groupName">The search group name.</param>
    /// <returns>The FTS5 table name.</returns>
    internal string? FindIndexTable(string groupName) =>
        _indexedGroups.TryGetValue(groupName, out var table) ? table : null;

    /// <summary>Whether searching this language unstemmed has been accepted explicitly.</summary>
    /// <param name="language">The language code.</param>
    /// <returns><see langword="true"/> when the application accepted it.</returns>
    internal bool IsUnstemmedAccepted(string? language) =>
        language is not null && _unstemmedLanguages.Contains(language);
}
