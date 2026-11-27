using Bsft.Data.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bsft.Localization;

/// <summary>
/// The default <see cref="ITranslationChain"/>: tenant row, global row and seed for a locale, then
/// the same three for the fallback locale.
/// </summary>
/// <remarks>
/// <para>
/// One implementation, so that the string localizer and the culture-explicit lookup differ only in
/// where the locale and the tenant come from (the ambient culture and request for the localizer, the
/// arguments for the lookup) and in whether a miss is remembered, never in what a key resolves to.
/// </para>
/// <para>
/// The hit cache is the shared <see cref="IMemoryCache"/> and holds hits only. Misses are not
/// cached here: a caller that wants them remembered keeps them in a cache of its own.
/// </para>
/// </remarks>
internal sealed class TranslationChain : ITranslationChain
{
    // A resolved translation is cached for this long. Long enough to keep the lookup off the
    // database, short enough that a change published without an invalidation event still lands.
    private static readonly TimeSpan HitLifetime = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _requests;
    private readonly TranslationSeed _seed;
    private readonly IMemoryCache _cache;
    private readonly BsftLocalizationOptions _localeOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationChain"/> class from the registered options.
    /// </summary>
    /// <param name="scopeFactory">The scope factory for resolving the scoped reader outside a request.</param>
    /// <param name="requests">The ambient request, when the lookup happens inside one.</param>
    /// <param name="seed">The seed translations.</param>
    /// <param name="cache">The memory cache holding resolved translations.</param>
    /// <param name="localeOptions">The localization options carrying the default and fallback locales.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public TranslationChain(
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor requests,
        TranslationSeed seed,
        IMemoryCache cache,
        IOptions<BsftLocalizationOptions> localeOptions)
        : this(scopeFactory, requests, seed, cache, localeOptions?.Value!)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationChain"/> class.
    /// </summary>
    /// <param name="scopeFactory">The scope factory for resolving the scoped reader outside a request.</param>
    /// <param name="requests">The ambient request, when the lookup happens inside one.</param>
    /// <param name="seed">The seed translations.</param>
    /// <param name="cache">The memory cache holding resolved translations.</param>
    /// <param name="localeOptions">The locale settings including default and fallback locales.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    internal TranslationChain(
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
    public string FallbackLocale => _localeOptions.FallbackLocale;

    /// <inheritdoc />
    public TimeSpan CacheLifetime => HitLifetime;

    /// <inheritdoc />
    public string LocaleOrDefault(string locale)
        => string.IsNullOrEmpty(locale) ? _localeOptions.DefaultLocale : locale;

    /// <inheritdoc />
    public bool IsConfiguredLocale(string locale)
        => string.Equals(locale, _localeOptions.DefaultLocale, StringComparison.OrdinalIgnoreCase)
           || string.Equals(locale, _localeOptions.FallbackLocale, StringComparison.OrdinalIgnoreCase)
           || _seed.HasLocale(locale);

    /// <inheritdoc />
    public bool TryGetCached(string? tenantId, string locale, string key, out string? value)
        => _cache.TryGetValue(new TranslationCacheKey(tenantId, locale, key), out value);

    /// <inheritdoc />
    public string? Load(IServiceProvider services, string? tenantId, string locale, string key)
    {
        var value = Resolve(services, tenantId, locale, key);

        // Only cache positive results here. Caching a miss in the shared cache would hide a key that is
        // added later (e.g. via seeding) for up to the cache lifetime, since seeding does not
        // invalidate it.
        if (value is not null)
            _cache.Set(new TranslationCacheKey(tenantId, locale, key), value, HitLifetime);

        return value;
    }

    /// <inheritdoc />
    public string? LoadCanonical(IServiceProvider services, string? tenantId, string locale, string key)
    {
        var match = ResolveMatch(services, tenantId, locale, key);

        if (match is null)
            return null;

        // Cached only when the caller's spelling is the one the store or seed holds. Any other spelling
        // resolves to the same value, but an entry for it would be one more entry per spelling.
        if (IsHeldSpelling(match.Value, locale, key))
            _cache.Set(new TranslationCacheKey(tenantId, locale, key), match.Value.Value, HitLifetime);

        return match.Value.Value;
    }

    /// <inheritdoc />
    public string? Resolve(IServiceProvider services, string? tenantId, string locale, string key)
        => ResolveMatch(services, tenantId, locale, key)?.Value;

    private TranslationMatch? ResolveMatch(IServiceProvider services, string? tenantId, string locale, string key)
    {
        // Load just the candidate rows for this key once, then resolve through the same in-memory
        // chain used by the bundle (avoids two separate store reads per fallback level).
        var rows = services.GetRequiredService<ITranslationReader>()
            .ListForKey(key, locale, _localeOptions.FallbackLocale, tenantId);

        return FindMatch(rows, key, locale, tenantId);
    }

    // A match at the requested locale is held under the requested spelling when its locale and key read
    // the same. A match at the fallback locale says nothing about how the requested locale is spelled, so
    // that spelling must be one the host configured instead.
    private bool IsHeldSpelling(TranslationMatch match, string locale, string key)
    {
        if (!string.Equals(match.Key, key, StringComparison.Ordinal))
            return false;

        return match.IsFallback
            ? string.Equals(locale, _localeOptions.DefaultLocale, StringComparison.Ordinal)
              || string.Equals(locale, _localeOptions.FallbackLocale, StringComparison.Ordinal)
              || _seed.HasLocaleSpelling(locale)
            : string.Equals(match.Locale, locale, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The result is materialised inside the call rather than streamed out of it, because the scope
    /// this may have opened is gone by the time the caller looks at what it got.
    /// </remarks>
    public TResult Read<TResult>(Func<IServiceProvider, TResult> read)
    {
        var ambient = _requests.HttpContext?.RequestServices;

        if (ambient is not null)
            return read(ambient);

        using var scope = _scopeFactory.CreateScope();

        return read(scope.ServiceProvider);
    }

    /// <inheritdoc />
    public string? ResolveTenantId(IServiceProvider services)
    {
        var tenantContext = services.GetService<ITenantContext>();

        return tenantContext is not null && tenantContext.IsResolved
            ? tenantContext.TenantId
            : null;
    }

    /// <inheritdoc />
    public string? ResolveFromLoaded(IReadOnlyList<Translation> loaded, string key, string locale, string? tenantId)
        => FindMatch(loaded, key, locale, tenantId)?.Value;

    private TranslationMatch? FindMatch(IReadOnlyList<Translation> loaded, string key, string locale, string? tenantId)
    {
        var match = FindMatchForLocale(loaded, key, locale, tenantId, isFallback: false);
        if (match is not null)
            return match;

        // Fall back to the fallback locale. When locale already is the fallback locale this simply
        // repeats the (failed) lookup and returns null, which keeps the control flow branch-free.
        return FindMatchForLocale(loaded, key, _localeOptions.FallbackLocale, tenantId, isFallback: true);
    }

    private TranslationMatch? FindMatchForLocale(
        IReadOnlyList<Translation> loaded,
        string key,
        string locale,
        string? tenantId,
        bool isFallback)
    {
        if (tenantId is not null)
        {
            var tenantRow = Match(loaded, key, locale, tenantId);
            if (tenantRow is not null)
                return new TranslationMatch(tenantRow.Value, tenantRow.Key, tenantRow.Locale, isFallback);
        }

        var globalRow = Match(loaded, key, locale, tenantId: null);
        if (globalRow is not null)
            return new TranslationMatch(globalRow.Value, globalRow.Key, globalRow.Locale, isFallback);

        return _seed.TryGetEntry(locale, key, out var heldLocale, out var heldKey, out var seedValue)
            ? new TranslationMatch(seedValue, heldKey, heldLocale, isFallback)
            : null;
    }

    /// <summary>
    /// Finds the row of one tenant, or of none, among the rows that were loaded for a key.
    /// </summary>
    /// <param name="loaded">The rows the store returned.</param>
    /// <param name="key">The translation key to match.</param>
    /// <param name="locale">The locale to match.</param>
    /// <param name="tenantId">The tenant to match, or <see langword="null"/> for a global row.</param>
    /// <returns>The matching row, or <see langword="null"/> when no row matches.</returns>
    /// <remarks>
    /// Keys and locale codes are matched case insensitively, the way the rest of this package treats
    /// them: a row stored as <c>EN</c> is the locale a caller asks for as <c>en</c>, and a bundle
    /// that deduplicates the two spellings into one locale has to resolve both to the same rows. The
    /// tenant is matched exactly, because it is an identity rather than a name whose spelling the
    /// caller chose.
    /// </remarks>
    private static Translation? Match(IReadOnlyList<Translation> loaded, string key, string locale, string? tenantId)
    {
        // A loop rather than FirstOrDefault: this runs once per key of a bundle, and the predicate
        // would close over the three values and allocate for each of them.
        for (var index = 0; index < loaded.Count; index++)
        {
            var candidate = loaded[index];

            if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Locale, locale, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.TenantId, tenantId, StringComparison.Ordinal))
                return candidate;
        }

        return null;
    }
}
