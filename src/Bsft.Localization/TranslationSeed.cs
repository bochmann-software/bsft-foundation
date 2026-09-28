namespace Bsft.Localization;

/// <summary>
/// Holds all seed translation data collected by <see cref="Seeding.TranslationSeedBuilder"/>.
/// Seed entries are applied at startup using an insert-if-not-exists strategy.
/// </summary>
public sealed class TranslationSeed
{
    private readonly Dictionary<string, Dictionary<string, string>> _entries = new(StringComparer.OrdinalIgnoreCase);

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
        }

        localeEntries[key] = value;
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
