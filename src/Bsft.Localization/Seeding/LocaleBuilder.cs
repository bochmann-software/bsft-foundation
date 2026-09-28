using System.Reflection;
using System.Text.Json;

namespace Bsft.Localization.Seeding;

/// <summary>
/// Provides a fluent API for adding translation seed entries for a specific locale.
/// </summary>
public sealed class LocaleBuilder
{
    private readonly TranslationSeed _seed;
    private readonly string _locale;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocaleBuilder"/> class.
    /// </summary>
    /// <param name="seed">The translation seed to add entries to.</param>
    /// <param name="locale">The locale code these entries belong to.</param>
    internal LocaleBuilder(TranslationSeed seed, string locale)
    {
        _seed = seed ?? throw new ArgumentNullException(nameof(seed));
        _locale = locale ?? throw new ArgumentNullException(nameof(locale));
    }

    /// <summary>
    /// Adds a single translation entry.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <param name="value">The translated text.</param>
    /// <returns>This builder for chaining.</returns>
    public LocaleBuilder Add(string key, string value)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        if (value is null)
            throw new ArgumentNullException(nameof(value));

        _seed.Add(_locale, key, value);
        return this;
    }

    /// <summary>
    /// Loads translation entries from an embedded JSON resource.
    /// The JSON file must be a flat object mapping keys to string values.
    /// </summary>
    /// <param name="assembly">The assembly containing the embedded resource.</param>
    /// <param name="resourcePrefix">
    /// The resource name prefix. The full resource name is resolved by searching
    /// for embedded resources that start with this prefix and end with <c>.json</c>.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    public LocaleBuilder FromEmbeddedJson(Assembly assembly, string resourcePrefix)
    {
        if (assembly is null)
            throw new ArgumentNullException(nameof(assembly));

        if (resourcePrefix is null)
            throw new ArgumentNullException(nameof(resourcePrefix));

        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase)
                                 && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
            throw new InvalidOperationException(
                $"No embedded JSON resource found with prefix '{resourcePrefix}' in assembly '{assembly.GetName().Name}'.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);

        if (entries is null)
            return this;

        foreach (var kvp in entries)
        {
            _seed.Add(_locale, kvp.Key, kvp.Value);
        }

        return this;
    }
}
