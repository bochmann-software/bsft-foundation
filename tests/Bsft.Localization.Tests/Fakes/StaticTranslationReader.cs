namespace Bsft.Localization.Tests.Fakes;

/// <summary>
/// A store that hands the resolution chain exactly the rows a test names, whatever it asks for.
/// </summary>
/// <remarks>
/// The contract has each read return "every row that could answer" the question, and it is the store
/// that decides how strictly it matched — a SQL Server database under its default collation answers
/// a request for <c>en</c> with the rows stored as <c>EN</c>. This stands in for such a store, which
/// the in-memory provider behind the EF Core reader cannot imitate.
/// </remarks>
internal sealed class StaticTranslationReader : ITranslationReader
{
    private readonly IReadOnlyList<Translation> _rows;

    /// <summary>Initializes a new instance of the <see cref="StaticTranslationReader"/> class.</summary>
    /// <param name="rows">The rows every read answers with.</param>
    public StaticTranslationReader(params Translation[] rows) => _rows = rows;

    /// <inheritdoc />
    public IReadOnlyList<Translation> ListForLocale(string locale, string? tenantId) => _rows;

    /// <inheritdoc />
    public IReadOnlyList<Translation> ListForKey(string key, string locale, string fallbackLocale, string? tenantId)
        => _rows;

    /// <inheritdoc />
    public IReadOnlyList<Translation> ListForLocaleOrFallback(string locale, string fallbackLocale, string? tenantId)
        => _rows;

    /// <inheritdoc />
    public IReadOnlyList<string> ListLocales() => _rows.Select(row => row.Locale).Distinct().ToList();
}
