namespace Bsft.Localization;

/// <summary>
/// The resolution chain the string localizer and the culture-explicit lookup read through: tenant
/// row, global row and seed for a locale, then the same three for the fallback locale.
/// </summary>
/// <remarks>
/// Both callers are handed the one registered instance, so they differ only in where the locale and
/// the tenant come from and in whether a miss is remembered, never in what a key resolves to.
/// </remarks>
internal interface ITranslationChain
{
    /// <summary>Gets the fallback locale the chain falls back to.</summary>
    string FallbackLocale { get; }

    /// <summary>Gets how long a resolved translation or bundle stays in the shared hit cache.</summary>
    TimeSpan CacheLifetime { get; }

    /// <summary>
    /// Replaces a blank locale with the configured default.
    /// </summary>
    /// <param name="locale">The locale code, possibly empty (the invariant culture's name).</param>
    /// <returns>The locale to resolve under.</returns>
    string LocaleOrDefault(string locale);

    /// <summary>
    /// Tells whether a locale is one the host configured: the default locale, the fallback locale or
    /// a locale a seed holds.
    /// </summary>
    /// <param name="locale">The locale code, compared case insensitively.</param>
    /// <returns><see langword="true"/> when the locale is configured.</returns>
    /// <remarks>
    /// A caller-named locale outside this set may still have rows in the store, so it resolves like any
    /// other, but a caller that caches what it resolves must not key that cache by it: the set of
    /// locales a caller can invent is unbounded.
    /// </remarks>
    bool IsConfiguredLocale(string locale);

    /// <summary>
    /// Reads a translation that is already in the hit cache.
    /// </summary>
    /// <param name="tenantId">The tenant it was resolved for, or <see langword="null"/> for none.</param>
    /// <param name="locale">The locale it was resolved for.</param>
    /// <param name="key">The translation key.</param>
    /// <param name="value">The cached value, when there is one.</param>
    /// <returns><see langword="true"/> when the hit cache holds the key.</returns>
    /// <remarks>Touches no store and opens no scope. The lookup allocates for the boxed cache key.</remarks>
    bool TryGetCached(string? tenantId, string locale, string key, out string? value);

    /// <summary>
    /// Resolves a key through the full fallback chain against the store and caches a hit.
    /// </summary>
    /// <param name="services">The scoped provider to read the translation store from.</param>
    /// <param name="tenantId">The tenant to resolve for, or <see langword="null"/> for none.</param>
    /// <param name="locale">The locale to resolve for.</param>
    /// <param name="key">The translation key.</param>
    /// <returns>The resolved value, or <see langword="null"/> when no level of the chain has it.</returns>
    string? Load(IServiceProvider services, string? tenantId, string locale, string key);

    /// <summary>
    /// Resolves a key through the full fallback chain against the store and caches a hit only under a
    /// spelling the store or seed holds.
    /// </summary>
    /// <param name="services">The scoped provider to read the translation store from.</param>
    /// <param name="tenantId">The tenant to resolve for, or <see langword="null"/> for none.</param>
    /// <param name="locale">The locale to resolve for.</param>
    /// <param name="key">The translation key.</param>
    /// <returns>The resolved value, or <see langword="null"/> when no level of the chain has it.</returns>
    /// <remarks>
    /// The chain matches keys and locales ignoring case, so a spelling the caller made up resolves like
    /// the real one. It is answered but not cached: the key and the locale must equal, ordinally, the
    /// spelling of the row or seed entry that matched (for a fallback match the locale must be a
    /// configured spelling instead). What it returns is what <see cref="Load"/> returns.
    /// </remarks>
    string? LoadCanonical(IServiceProvider services, string? tenantId, string locale, string key);

    /// <summary>
    /// Resolves a key through the full fallback chain against the store without caching anything.
    /// </summary>
    /// <param name="services">The scoped provider to read the translation store from.</param>
    /// <param name="tenantId">The tenant to resolve for, or <see langword="null"/> for none.</param>
    /// <param name="locale">The locale to resolve for.</param>
    /// <param name="key">The translation key.</param>
    /// <returns>The resolved value, or <see langword="null"/> when no level of the chain has it.</returns>
    string? Resolve(IServiceProvider services, string? tenantId, string locale, string key);

    /// <summary>
    /// Runs a read against the services of the request being served, or of a scope opened for it.
    /// </summary>
    /// <typeparam name="TResult">What the read returns.</typeparam>
    /// <param name="read">The read, given the services to resolve the scoped store and tenant from.</param>
    /// <returns>What the read returned.</returns>
    TResult Read<TResult>(Func<IServiceProvider, TResult> read);

    /// <summary>
    /// Reads the tenant of the current request, if there is one.
    /// </summary>
    /// <param name="services">The scoped provider to read the tenant context from.</param>
    /// <returns>The tenant id, or <see langword="null"/> when the request runs in no tenant.</returns>
    string? ResolveTenantId(IServiceProvider services);

    /// <summary>
    /// Resolves a key against rows that were already loaded, applying the full fallback chain.
    /// </summary>
    /// <param name="loaded">The rows the store returned.</param>
    /// <param name="key">The translation key.</param>
    /// <param name="locale">The locale to resolve for.</param>
    /// <param name="tenantId">The tenant to resolve for, or <see langword="null"/> for none.</param>
    /// <returns>The resolved value, or <see langword="null"/> when no level of the chain has it.</returns>
    string? ResolveFromLoaded(IReadOnlyList<Translation> loaded, string key, string locale, string? tenantId);
}
