namespace Bsft.Localization;

/// <summary>
/// Holds all seed translation data collected by <see cref="Seeding.TranslationSeedBuilder"/>.
/// Seed entries are applied at startup using an insert-if-not-exists strategy.
/// </summary>
public sealed class TranslationSeed
{
    private readonly Dictionary<string, Dictionary<string, string>> _entries = new(StringComparer.OrdinalIgnoreCase);

    // The spelling each locale and key was first added under. The entries match case insensitively and
    // the dictionaries keep no way to read the key they hold, so the spelling is kept beside them.
    private readonly Dictionary<string, string> _localeSpellings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _keySpellings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a single translation seed entry.
    /// </summary>
    /// <param name="locale">The locale code.</param>
    /// <param name="key">The translation key.</param>
    /// <param name="value">The translation value.</param>
    internal void Add(string locale, string key, string value)
    {
        if (!_entries.TryGetValue(locale, out var localeEntries))
        {
            localeEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _entries[locale] = localeEntries;
            _localeSpellings[locale] = locale;
            _keySpellings[locale] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        localeEntries[key] = value;
        _keySpellings[locale].TryAdd(key, key);
    }

    /// <summary>
    /// Gets all seed entries grouped by locale.
    /// </summary>
    /// <returns>A dictionary mapping locale codes to their key-value translation pairs.</returns>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GetEntries()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _entries)
        {
            result[kvp.Key] = kvp.Value.AsReadOnly();
        }
        return result;
    }

    /// <summary>
    /// Tells whether any seed entry exists for a locale.
    /// </summary>
    /// <param name="locale">The locale code, compared case insensitively.</param>
    /// <returns><c>true</c> if the seed holds the locale; otherwise <c>false</c>.</returns>
    internal bool HasLocale(string locale) => _entries.ContainsKey(locale);

    /// <summary>
    /// Tells whether a locale is held under exactly this spelling.
    /// </summary>
    /// <param name="locale">The locale code, compared case sensitively.</param>
    /// <returns><c>true</c> if the seed holds the locale spelled so; otherwise <c>false</c>.</returns>
    internal bool HasLocaleSpelling(string locale)
        => _localeSpellings.TryGetValue(locale, out var spelling) && string.Equals(spelling, locale, StringComparison.Ordinal);

    /// <summary>
    /// Tries to retrieve a seed value together with the spelling of the locale and key it is held under.
    /// </summary>
    /// <param name="locale">The locale code, matched case insensitively.</param>
    /// <param name="key">The translation key, matched case insensitively.</param>
    /// <param name="heldLocale">The spelling of the locale the entry is held under.</param>
    /// <param name="heldKey">The spelling of the key the entry is held under.</param>
    /// <param name="value">The seed value if found.</param>
    /// <returns><c>true</c> if a seed exists for the given locale and key; otherwise <c>false</c>.</returns>
    internal bool TryGetEntry(string locale, string key, out string heldLocale, out string heldKey, out string value)
    {
        heldLocale = string.Empty;
        heldKey = string.Empty;
        value = string.Empty;

        if (!_entries.TryGetValue(locale, out var localeEntries) || !localeEntries.TryGetValue(key, out var found))
            return false;

        heldLocale = _localeSpellings[locale];
        heldKey = _keySpellings[locale][key];
        value = found;

        return true;
    }

    /// <summary>
    /// Tries to retrieve a seed value for the given locale and key.
    /// </summary>
    /// <param name="locale">The locale code.</param>
    /// <param name="key">The translation key.</param>
    /// <param name="value">The seed value if found.</param>
    /// <returns><c>true</c> if a seed exists for the given locale and key; otherwise <c>false</c>.</returns>
    public bool TryGetValue(string locale, string key, out string? value)
    {
        value = null;
        if (_entries.TryGetValue(locale, out var localeEntries))
        {
            return localeEntries.TryGetValue(key, out value);
        }
        return false;
    }
}
