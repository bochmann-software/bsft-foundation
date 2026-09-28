using System.Globalization;
using Bsft.Data.MultiTenancy;
using Bsft.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Bsft.Localization;

/// <summary>
/// A string localizer that resolves translations through a multi-level fallback chain:
/// tenant store, global store (TenantId null), seeds, fallback locale, and finally the key itself.
/// Results are cached per locale/tenant combination and invalidated when
/// <see cref="TranslationsUpdatedEvent"/> is published.
/// </summary>
/// <remarks>
/// The localizer is handed out by a singleton factory, so the scoped services it reads — the
/// translation store and the tenant context — are resolved per call. They are resolved out of the
/// request being served when there is one, and out of a scope of its own when there is not. That
/// distinction is not an optimisation: <see cref="ITenantContext"/> is scoped state that the tenant
/// middleware writes onto the request's scope, so a scope opened beside the request would answer
/// with an unresolved context and every tenant would be served the global bundle.
/// </remarks>
public sealed class BsftStringLocalizer : IStringLocalizer, ITranslationBundleReader
{
    // A resolved translation is cached for this long. Long enough to keep the lookup off the
    // database, short enough that a change published without an invalidation event still lands.
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _requests;
    private readonly TranslationSeed _seed;
    private readonly IMemoryCache _cache;
    private readonly BsftLocalizationOptions _localeOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftStringLocalizer"/> class.
    /// </summary>
    /// <param name="scopeFactory">The scope factory for resolving the scoped reader and tenant context outside a request.</param>
    /// <param name="requests">The ambient request, when the lookup happens inside one.</param>
    /// <param name="seed">The seed translations to use as a fallback before falling back to the key.</param>
    /// <param name="cache">The memory cache for caching resolved translations.</param>
    /// <param name="localeOptions">The locale settings including default and fallback locales.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public BsftStringLocalizer(
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor requests,
        TranslationSeed seed,
        IMemoryCache cache,
        BsftLocalizationOptions localeOptions)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        _seed = seed ?? throw new ArgumentNullException(nameof(seed));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _localeOptions = localeOptions ?? throw new ArgumentNullException(nameof(localeOptions));
    }

    /// <inheritdoc />
    public LocalizedString this[string name]
    {
        get
        {
            var value = Resolve(name, CultureInfo.CurrentUICulture.Name);
            return new LocalizedString(name, value ?? name, value is null);
        }
    }

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var value = Resolve(name, CultureInfo.CurrentUICulture.Name);
            var formatted = value is not null ? FormatValue(value, arguments) : name;
            return new LocalizedString(name, formatted, value is null);
        }
    }

    // Translation values come from the store/seeds, so a malformed format string (e.g. a literal
    // '{' or fewer arguments than placeholders) must not throw FormatException into the caller.
    private static string FormatValue(string value, object[] arguments)
    {
        // A params parameter can be handed a null array explicitly, and that is a caller who passed
        // no arguments rather than an error worth throwing out of a lookup that found its value.
        if (arguments is null)
            return value;

        try
        {
            return string.Format(CultureInfo.CurrentCulture, value, arguments);
        }
        catch (FormatException)
        {
            return value;
        }
    }

    /// <inheritdoc />
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var locale = CultureInfo.CurrentUICulture.Name;

        return Read(services =>
        {
            var reader = services.GetRequiredService<ITranslationReader>();
            var tenantId = ResolveTenantId(services);

            return reader.ListForLocale(locale, tenantId)
                .Select(translation => new LocalizedString(translation.Key, translation.Value, false))
                .ToList();
        });
    }

    /// <summary>
    /// Resolves a translation value for the given key and locale using the full fallback chain.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <param name="locale">The locale to resolve for.</param>
    /// <returns>The resolved value, or <c>null</c> if no translation was found at any level.</returns>
    internal string? Resolve(string key, string locale)
    {
        if (string.IsNullOrEmpty(locale))
            locale = _localeOptions.DefaultLocale;

        return Read(services =>
        {
            var tenantId = ResolveTenantId(services);
            var cacheKey = new TranslationCacheKey(tenantId, locale, key);

            if (_cache.TryGetValue(cacheKey, out string? cached))
                return cached;

            // Load just the candidate rows for this key once, then resolve through the same in-memory
            // chain used by ResolveAll (avoids two separate store reads per fallback level).
            var rows = services.GetRequiredService<ITranslationReader>()
                .ListForKey(key, locale, _localeOptions.FallbackLocale, tenantId);

            var value = ResolveFromLoaded(rows, key, locale, tenantId);

            // Only cache positive results. Caching a miss would otherwise hide a key that is added later
            // (e.g. via seeding) for up to the cache lifetime, since seeding does not invalidate the cache.
            if (value is not null)
                _cache.Set(cacheKey, value, CacheLifetime);

            return value;
        });
    }

    /// <inheritdoc cref="ITranslationBundleReader.ListAvailableLocales" />
    /// <remarks>
    /// Declared on the type as well as on the contract because the bundle types it deals in are
    /// internal to this package, which a public member may not name.
    /// </remarks>
    internal IReadOnlyList<string> ListAvailableLocales()
    {
        var storedLocales = Read(services => services.GetRequiredService<ITranslationReader>().ListLocales());

        var seedLocales = _seed.GetEntries().Keys;

        return storedLocales
            .Union(seedLocales, StringComparer.OrdinalIgnoreCase)
            .OrderBy(locale => locale, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc cref="ITranslationBundleReader.ResolveBundle" />
    /// <remarks>
    /// <para>
    /// The bundle and its version live in one entry of the same <see cref="IMemoryCache"/> the
    /// per-key lookups use. That is the whole design: <see cref="TranslationCacheInvalidator"/>
    /// already compacts that cache when <see cref="TranslationsUpdatedEvent"/> is published, so the
    /// bundle's version is dropped by the invalidation path that exists rather than by a second one
    /// that would have to be kept in step with it. The next request re-materialises the bundle from
    /// the store and hashes what it found, which is a different hash exactly when the content is
    /// different.
    /// </para>
    /// <para>
    /// Hashing at materialisation time rather than per request is what makes a revalidation cheap:
    /// a conditional request that hits the cache costs a dictionary lookup and a string comparison,
    /// not a re-hash of every translation in the locale.
    /// </para>
    /// </remarks>
    internal TranslationBundle ResolveBundle(string locale) =>
        Read(services =>
        {
            var tenantId = ResolveTenantId(services);
            var cacheKey = new BundleCacheKey(tenantId, locale);

            if (_cache.TryGetValue(cacheKey, out TranslationBundle? cached) && cached is not null)
                return cached;

            var translations = ResolveAllForTenant(services, locale, tenantId);
            var bundle = new TranslationBundle(translations, ComputeVersion(locale, tenantId, translations));

            _cache.Set(cacheKey, bundle, CacheLifetime);

            return bundle;
        });

    /// <inheritdoc />
    IReadOnlyList<string> ITranslationBundleReader.ListAvailableLocales() => ListAvailableLocales();

    /// <inheritdoc />
    TranslationBundle ITranslationBundleReader.ResolveBundle(string locale) => ResolveBundle(locale);

    /// <summary>
    /// Runs a read against the services of the request being served, or of a scope opened for it.
    /// </summary>
    /// <typeparam name="TResult">What the read returns.</typeparam>
    /// <param name="read">The read, given the services to resolve the scoped store and tenant from.</param>
    /// <returns>What the read returned.</returns>
    /// <remarks>
    /// The result is materialised inside the call rather than streamed out of it, because the scope
    /// this may have opened is gone by the time the caller looks at what it got.
    /// </remarks>
    private TResult Read<TResult>(Func<IServiceProvider, TResult> read)
    {
        var ambient = _requests.HttpContext?.RequestServices;

        if (ambient is not null)
            return read(ambient);

        using var scope = _scopeFactory.CreateScope();

        return read(scope.ServiceProvider);
    }

    /// <summary>
    /// Reads the tenant of the current request, if there is one.
    /// </summary>
    /// <param name="services">The scoped provider to read the tenant context from.</param>
    /// <returns>The tenant id, or <see langword="null"/> when the request runs in no tenant.</returns>
    private static string? ResolveTenantId(IServiceProvider services)
    {
        var tenantContext = services.GetService<ITenantContext>();

        return tenantContext is not null && tenantContext.IsResolved
            ? tenantContext.TenantId
            : null;
    }

    /// <summary>
    /// Reduces a bundle to the version that identifies it.
    /// </summary>
    /// <param name="locale">The locale the bundle was resolved for.</param>
    /// <param name="tenantId">The tenant the bundle was resolved for, or <see langword="null"/>.</param>
    /// <param name="translations">The resolved content.</param>
    /// <returns>The SHA-256 of the bundle, as hexadecimal.</returns>
    /// <remarks>
    /// <para>
    /// The locale and the tenant are hashed alongside the content, not merely used to pick the
    /// cache entry. The URL of the bundle names the locale but never the tenant, so a tag built out
    /// of the content alone would be the same tag for two tenants whose translations happen to
    /// agree — and a client that switched tenant mid-session would be told 304 for a bundle it was
    /// not holding. Folding both into the hash makes the tag identify the representation, not just
    /// its bytes.
    /// </para>
    /// <para>
    /// Keys are ordinal-sorted before hashing. The dictionary is ordinal-ignore-case and its
    /// enumeration order follows insertion, which follows a <see cref="HashSet{T}"/>, which is not
    /// a promise — two servers resolving the same content could otherwise hash it into two
    /// different versions.
    /// </para>
    /// </remarks>
    private static string ComputeVersion(string locale, string? tenantId, IReadOnlyDictionary<string, string> translations)
        => VersionSegments(locale, tenantId, translations).ToCanonicalHash();

    /// <summary>
    /// Streams the bundle into the fixed sequence of segments its version is taken over.
    /// </summary>
    /// <param name="locale">The locale the bundle was resolved for.</param>
    /// <param name="tenantId">The tenant the bundle was resolved for, or <see langword="null"/>.</param>
    /// <param name="translations">The resolved content.</param>
    /// <returns>The segments, in the order they are hashed in.</returns>
    private static IEnumerable<string?> VersionSegments(
        string locale,
        string? tenantId,
        IReadOnlyDictionary<string, string> translations)
    {
        yield return locale;
        yield return tenantId;

        // The list is preceded by its length, so that the segments framing it can never be read as
        // part of it. Uniform with how Bsft.Auth frames the lists in its own version.
        yield return translations.Count.ToString(CultureInfo.InvariantCulture);

        foreach (var entry in translations.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            yield return entry.Key;
            yield return entry.Value;
        }
    }

    /// <summary>
    /// Resolves every key of a locale for one tenant, applying the full fallback chain.
    /// </summary>
    /// <param name="services">The scoped provider to read the translation store from.</param>
    /// <param name="locale">The locale to resolve translations for.</param>
    /// <param name="tenantId">The tenant to resolve for, or <see langword="null"/> for the global bundle.</param>
    /// <returns>A dictionary of all resolved key-value pairs.</returns>
    private Dictionary<string, string> ResolveAllForTenant(IServiceProvider services, string locale, string? tenantId)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var reader = services.GetRequiredService<ITranslationReader>();

        // Collect all keys from seeds
        var seedEntries = _seed.GetEntries();
        var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (seedEntries.TryGetValue(locale, out var localeSeedEntries))
        {
            foreach (var key in localeSeedEntries.Keys)
                allKeys.Add(key);
        }

        if (!string.Equals(locale, _localeOptions.FallbackLocale, StringComparison.OrdinalIgnoreCase)
            && seedEntries.TryGetValue(_localeOptions.FallbackLocale, out var fallbackSeedEntries))
        {
            foreach (var key in fallbackSeedEntries.Keys)
                allKeys.Add(key);
        }

        // Collect all keys from the store
        var storedEntries = reader.ListForLocaleOrFallback(locale, _localeOptions.FallbackLocale, tenantId);

        foreach (var entry in storedEntries)
            allKeys.Add(entry.Key);

        // Resolve each key against the already-loaded rows instead of issuing a read per key (N+1).
        foreach (var key in allKeys)
        {
            var value = ResolveFromLoaded(storedEntries, key, locale, tenantId);
            if (value is not null)
                result[key] = value;
        }

        return result;
    }

    private string? ResolveFromLoaded(IReadOnlyList<Translation> loaded, string key, string locale, string? tenantId)
    {
        var value = ResolveForLocaleFromLoaded(loaded, key, locale, tenantId);
        if (value is not null)
            return value;

        // Fall back to the fallback locale. When locale already is the fallback locale this simply
        // repeats the (failed) lookup and returns null, which keeps the control flow branch-free.
        return ResolveForLocaleFromLoaded(loaded, key, _localeOptions.FallbackLocale, tenantId);
    }

    private string? ResolveForLocaleFromLoaded(IReadOnlyList<Translation> loaded, string key, string locale, string? tenantId)
    {
        if (tenantId is not null)
        {
            var tenantValue = Match(loaded, key, locale, tenantId);
            if (tenantValue is not null)
                return tenantValue;
        }

        var globalValue = Match(loaded, key, locale, tenantId: null);
        if (globalValue is not null)
            return globalValue;

        return _seed.TryGetValue(locale, key, out var seedValue) ? seedValue : null;
    }

    /// <summary>
    /// Finds the row of one tenant, or of none, among the rows that were loaded for a key.
    /// </summary>
    /// <param name="loaded">The rows the store returned.</param>
    /// <param name="key">The translation key to match.</param>
    /// <param name="locale">The locale to match.</param>
    /// <param name="tenantId">The tenant to match, or <see langword="null"/> for a global row.</param>
    /// <returns>The matching value, or <see langword="null"/> when no row matches.</returns>
    /// <remarks>
    /// Keys and locale codes are matched case insensitively, the way the rest of this package treats
    /// them: a row stored as <c>EN</c> is the locale a caller asks for as <c>en</c>, and a bundle
    /// that deduplicates the two spellings into one locale has to resolve both to the same rows. The
    /// tenant is matched exactly, because it is an identity rather than a name whose spelling the
    /// caller chose.
    /// </remarks>
    private static string? Match(IReadOnlyList<Translation> loaded, string key, string locale, string? tenantId)
    {
        // A loop rather than FirstOrDefault: this runs once per key of a bundle, and the predicate
        // would close over the three values and allocate for each of them.
        for (var index = 0; index < loaded.Count; index++)
        {
            var candidate = loaded[index];

            if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Locale, locale, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.TenantId, tenantId, StringComparison.Ordinal))
                return candidate.Value;
        }

        return null;
    }

    /// <summary>What one resolved translation is cached under.</summary>
    /// <param name="TenantId">The tenant it was resolved for, or <see langword="null"/> for none.</param>
    /// <param name="Locale">The locale it was resolved for.</param>
    /// <param name="Key">The translation key.</param>
    /// <remarks>
    /// A key of its own rather than the three parts joined into a string. A joined key needs a stand-in
    /// for the absent tenant and a separator that none of the parts may contain, and neither holds:
    /// a tenant whose id is literally the stand-in, or a key containing the separator, would read
    /// another scope's entry. The parts are compared as parts here, so no spelling can collide.
    /// </remarks>
    private readonly record struct TranslationCacheKey(string? TenantId, string Locale, string Key);

    /// <summary>What one resolved bundle is cached under.</summary>
    /// <param name="TenantId">The tenant it was resolved for, or <see langword="null"/> for none.</param>
    /// <param name="Locale">The locale it was resolved for.</param>
    /// <remarks>
    /// A type of its own, so that a bundle entry can never collide with the single-key entry of a
    /// translation whose key happens to read like a locale: the two key types are never equal, whatever
    /// they carry.
    /// </remarks>
    private readonly record struct BundleCacheKey(string? TenantId, string Locale);
}
