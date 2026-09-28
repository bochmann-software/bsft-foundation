namespace Bsft.Localization.Seeding;

/// <summary>
/// Provides a fluent API for building translation seed data organized by locale.
/// </summary>
public sealed class TranslationSeedBuilder
{
    private readonly TranslationSeed _seed = new();

    /// <summary>
    /// Configures seed translations for the specified locale.
    /// </summary>
    /// <param name="locale">The locale code (e.g. "en", "de").</param>
    /// <param name="configure">A delegate that adds entries through <see cref="LocaleBuilder"/>.</param>
    /// <returns>This builder for chaining.</returns>
    public TranslationSeedBuilder ForLocale(string locale, Action<LocaleBuilder> configure)
    {
        if (locale is null)
            throw new ArgumentNullException(nameof(locale));

        if (configure is null)
            throw new ArgumentNullException(nameof(configure));

        var builder = new LocaleBuilder(_seed, locale);
        configure(builder);
        return this;
    }

    /// <summary>
    /// Builds the translation seed from the configured entries.
    /// </summary>
    /// <returns>The constructed <see cref="TranslationSeed"/>.</returns>
    public TranslationSeed Build() => _seed;
}
