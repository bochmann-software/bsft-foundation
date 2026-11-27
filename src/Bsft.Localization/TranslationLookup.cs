using System.Globalization;
using Microsoft.Extensions.Options;

namespace Bsft.Localization;

/// <summary>
/// The default <see cref="ITranslationLookup"/>: the shared resolution chain with an explicit tenant
/// and culture, plus a bounded cache of misses.
/// </summary>
/// <remarks>
/// <para>
/// Order of the answer: the shared hit cache, then the miss cache, then the store. The hit cache comes
/// first so that a key that was missed and has since been resolved (for instance by the localizer)
/// answers with its value rather than with the remembered miss.
/// </para>
/// <para>
/// A locale the host did not configure (neither the default, the fallback nor one a seed holds) is
/// resolved like any other, but neither its hits nor its misses are cached: the culture is the
/// caller's, the set of cultures a caller can name is unbounded, and a cache keyed by it would grow
/// by what the caller invents. Mapping such a locale onto a configured one before caching is not
/// possible without changing answers, because the store may hold rows for the locale the localizer
/// would find and the fallback would not.
/// </para>
/// <para>
/// A hit is cached only under the spelling the store or seed holds it under, so a key or locale
/// spelled in another case resolves to the localizer's answer but adds no entry.
/// </para>
/// </remarks>
internal sealed class TranslationLookup : ITranslationLookup
{
    private readonly ITranslationChain _chain;
    private readonly ITranslationMissCache _misses;
    private readonly int _maxKeyLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationLookup"/> class.
    /// </summary>
    /// <param name="chain">The resolution chain shared with the string localizer.</param>
    /// <param name="misses">The cache of keys that no level of the chain has.</param>
    /// <param name="options">The localization options carrying the longest key accepted.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public TranslationLookup(
        ITranslationChain chain,
        ITranslationMissCache misses,
        IOptions<BsftLocalizationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _chain = chain ?? throw new ArgumentNullException(nameof(chain));
        _misses = misses ?? throw new ArgumentNullException(nameof(misses));
        _maxKeyLength = (options.Value ?? throw new ArgumentNullException(nameof(options))).MaxKeyLength;
    }

    /// <inheritdoc />
    public string? Find(string? tenantId, CultureInfo culture, string key)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (key.Length > _maxKeyLength)
        {
            throw new ArgumentException(
                $"The key is {key.Length} characters long, but at most {_maxKeyLength} are accepted.", nameof(key));
        }

        var locale = _chain.LocaleOrDefault(culture.Name);

        if (_chain.TryGetCached(tenantId, locale, key, out var cached))
            return cached;

        if (!_chain.IsConfiguredLocale(locale))
            return _chain.Read(services => _chain.Resolve(services, tenantId, locale, key));

        var cacheKey = new TranslationCacheKey(tenantId, locale, key);

        if (_misses.Contains(cacheKey))
            return null;

        var value = _chain.Read(services => _chain.LoadCanonical(services, tenantId, locale, key));

        if (value is null)
            _misses.TryAdd(cacheKey);

        return value;
    }
}
