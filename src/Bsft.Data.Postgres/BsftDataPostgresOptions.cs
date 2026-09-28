using Bsft.Data.Search;

namespace Bsft.Data.Postgres;

/// <summary>
/// Declares which search groups are backed by a <c>tsvector</c> column and which column that is.
/// </summary>
/// <remarks>
/// <para>
/// The framework cannot inspect the schema at startup without a connection, so the application
/// states which groups it has indexed. A group that is declared searchable but not listed here fails
/// startup validation — the deliberate alternative to silently scanning the table.
/// </para>
/// <para>
/// This is the package's primary options type and it deliberately carries no <c>SectionName</c> and
/// binds from no configuration section. Every member of it describes the <em>schema</em>: which
/// column a migration generated, and which text search configuration was baked into that generated
/// column. A deployment that could state them separately could state them differently, and the
/// result would be queries stemmed by one configuration against an index built with another —
/// answering a narrower question than it was asked, which is the failure this package exists to
/// remove. The declaration is therefore written beside the migration that makes it true.
/// </para>
/// </remarks>
public sealed class BsftDataPostgresOptions
{
    /// <summary>The column name assumed when a group does not name its own.</summary>
    public const string DefaultVectorProperty = "SearchVector";

    /// <summary>The stemming language assumed when neither the index nor the deployment names one.</summary>
    public const string DefaultSearchLanguage = "en";

    private readonly Dictionary<string, string> _indexedGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _textSearchConfigurations = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<string>? _supportedLanguageCodes;

    /// <summary>
    /// Gets the declared groups mapped to the <c>tsvector</c> property backing each one.
    /// </summary>
    public IReadOnlyDictionary<string, string> IndexedGroups => _indexedGroups;

    /// <summary>
    /// Gets the language an index is built with when its <see cref="PostgresCreateFullTextIndexRequest"/>
    /// names none. A German-first deployment states this once instead of repeating <c>de</c> in every
    /// migration and losing on the one that forgets.
    /// </summary>
    public string DefaultLanguage { get; private set; } = DefaultSearchLanguage;

    /// <summary>
    /// Gets the text search configurations the application registered on top of the built-in map,
    /// keyed by language code.
    /// </summary>
    public IReadOnlyDictionary<string, string> TextSearchConfigurations => _textSearchConfigurations;

    /// <summary>
    /// Declares that a <c>tsvector</c> column with a GIN index exists for the given search group.
    /// </summary>
    /// <param name="groupName">The search group name.</param>
    /// <param name="vectorProperty">
    /// The entity property mapped to the <c>tsvector</c> column. It is usually a shadow property, so
    /// the entity is not forced to carry a database concept as a member.
    /// </param>
    /// <returns>The same options instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when an argument is null or whitespace.</exception>
    public BsftDataPostgresOptions HasFullTextIndex(string groupName, string vectorProperty = DefaultVectorProperty)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            throw new ArgumentException("A search group name is required.", nameof(groupName));
        if (string.IsNullOrWhiteSpace(vectorProperty))
            throw new ArgumentException("A tsvector property name is required.", nameof(vectorProperty));

        _indexedGroups[groupName] = vectorProperty;
        return this;
    }

    /// <summary>
    /// Sets the stemming language an index is built with when its command names none.
    /// </summary>
    /// <param name="language">A two-letter language code such as <c>de</c>.</param>
    /// <returns>The same options instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="language"/> is null or whitespace.</exception>
    /// <remarks>
    /// Only the code is checked here, not that it maps to a configuration. A deployment is free to
    /// set the default first and register the configuration for it afterwards, so ordering the two
    /// calls wrongly must not decide whether the application starts. An unmappable language is
    /// reported where it matters: the migration that would build the index fails to emit its DDL,
    /// and a group declaring it fails startup validation.
    /// </remarks>
    public BsftDataPostgresOptions UseDefaultLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("A language code is required.", nameof(language));

        DefaultLanguage = language;
        return this;
    }

    /// <summary>
    /// Maps a language code to a PostgreSQL text search configuration of the deployment's own.
    /// </summary>
    /// <param name="languageCode">The language code the search groups declare, such as <c>de</c>.</param>
    /// <param name="configurationName">
    /// The <c>regconfig</c> to use, such as an <c>unaccent</c>-composed <c>german_unaccent</c>. It is
    /// interpolated into the generated column's DDL as a literal, so it is restricted to letters,
    /// digits and underscores exactly as every other identifier this package emits. A
    /// schema-qualified name is therefore rejected; put the configuration on the <c>search_path</c>.
    /// </param>
    /// <returns>The same options instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when an argument is null or whitespace, or the configuration name is not a valid identifier.</exception>
    /// <remarks>
    /// A registration wins over the built-in mapping for the same code, which is what makes this an
    /// override and not merely an extension: a deployment that has built <c>german_unaccent</c> wants
    /// every group declaring <c>de</c> to use it, not only the codes the framework never knew.
    /// </remarks>
    public BsftDataPostgresOptions UseTextSearchConfiguration(string languageCode, string configurationName)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            throw new ArgumentException("A language code is required.", nameof(languageCode));

        _textSearchConfigurations[languageCode] =
            SqlIdentifier.Require(configurationName, nameof(configurationName));
        _supportedLanguageCodes = null;

        return this;
    }

    /// <summary>
    /// Copies this declaration onto the instance the container will hand to the provider.
    /// </summary>
    /// <param name="target">The instance the options pattern is building.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is null.</exception>
    /// <remarks>
    /// The registration takes a copy rather than putting the caller's instance in the container, so
    /// that a mutation after <c>AddBsftDataPostgres</c> cannot make the provider and the migration
    /// disagree about the same index. It goes through the public declaring methods, so a copy is
    /// checked exactly as the original was.
    /// </remarks>
    internal void CopyTo(BsftDataPostgresOptions target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.UseDefaultLanguage(DefaultLanguage);

        foreach (var (languageCode, configurationName) in _textSearchConfigurations)
            target.UseTextSearchConfiguration(languageCode, configurationName);

        foreach (var (groupName, vectorProperty) in _indexedGroups)
            target.HasFullTextIndex(groupName, vectorProperty);
    }

    /// <summary>Gets the <c>tsvector</c> property backing a group, or <see langword="null"/> when it is not declared.</summary>
    /// <param name="groupName">The search group name.</param>
    /// <returns>The property name.</returns>
    internal string? FindVectorProperty(string groupName) =>
        _indexedGroups.TryGetValue(groupName, out var property) ? property : null;

    /// <summary>
    /// Resolves the text search configuration for a language code, letting a registration override
    /// the built-in mapping.
    /// </summary>
    /// <param name="language">A two-letter language code, or <see langword="null"/>.</param>
    /// <returns>
    /// The configuration name, or <see langword="null"/> when the code is unknown or absent. The null
    /// result is deliberately kept: it leaves a query on the server's <c>default_text_search_config</c>
    /// rather than guessing wrong, and it is what startup validation reports on.
    /// </returns>
    internal string? ResolveConfiguration(string? language) =>
        language is not null && _textSearchConfigurations.TryGetValue(language, out var configuration)
            ? configuration
            : FullTextLanguageResolver.Resolve(language);

    /// <summary>
    /// Gets every language code this application can map, built-in and registered alike, in a stable
    /// order so an error message naming them reads the same on every machine.
    /// </summary>
    /// <remarks>
    /// Computed once and kept until a registration changes the set, because the one caller is an error
    /// message and rebuilding the list for every read would be work done to say the same thing.
    /// </remarks>
    internal IReadOnlyList<string> SupportedLanguageCodes =>
        _supportedLanguageCodes ??=
        [
            .. FullTextLanguageResolver.SupportedCodes
                .Concat(_textSearchConfigurations.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.Ordinal),
        ];
}
