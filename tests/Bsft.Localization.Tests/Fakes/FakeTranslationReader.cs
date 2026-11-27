namespace Bsft.Localization.Tests.Fakes;

/// <summary>
/// A store a test can add rows to between reads, which counts the key reads the resolution chain makes.
/// </summary>
/// <remarks>
/// The count is what the miss cache and the hit cache are judged by: a read that does not reach the
/// store leaves it unchanged. The rows are returned whole, as the store contract asks, and the chain
/// does the matching.
/// </remarks>
internal sealed class FakeTranslationReader : ITranslationReader
{
    private readonly List<Translation> _rows;

    /// <summary>Initializes a new instance of the <see cref="FakeTranslationReader"/> class.</summary>
    /// <param name="rows">The rows the store starts with.</param>
    public FakeTranslationReader(params Translation[] rows) => _rows = rows.ToList();

    /// <summary>Gets how many times the store was asked for the rows of one key.</summary>
    public int KeyReads { get; private set; }

    /// <summary>Adds a row, as a seeding or an editor would between two reads.</summary>
    /// <param name="row">The row to add.</param>
    public void Add(Translation row) => _rows.Add(row);

    /// <inheritdoc />
    public IReadOnlyList<Translation> ListForLocale(string locale, string? tenantId) => _rows.ToList();

    /// <inheritdoc />
    public IReadOnlyList<Translation> ListForKey(string key, string locale, string fallbackLocale, string? tenantId)
    {
        KeyReads++;

        return _rows.ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<Translation> ListForLocaleOrFallback(string locale, string fallbackLocale, string? tenantId)
        => _rows.ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> ListLocales() => _rows.Select(row => row.Locale).Distinct().ToList();
}
