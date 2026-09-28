namespace Bsft.Localization;

/// <summary>
/// Reads stored translations for the resolution chain.
/// </summary>
/// <remarks>
/// <para>
/// Four reads, one per question the localizer asks, rather than a general query surface: every
/// method names a whole answer the chain then resolves in memory, so a store implementation cannot
/// accidentally turn one lookup into a query per key. <c>Bsft.Localization.EFCore</c> is the
/// implementation the framework ships.
/// </para>
/// <para>
/// The reads are synchronous because <see cref="Microsoft.Extensions.Localization.IStringLocalizer"/>
/// is: its indexer returns a value, not a task, so an asynchronous store would have to be blocked on
/// here anyway, at a place with less context about it than the store has.
/// </para>
/// <para>
/// A tenant is passed rather than read from ambient state, and every read that takes one returns the
/// tenant's rows <em>and</em> the global rows (<c>TenantId</c> null), because the chain needs both to
/// decide which wins.
/// </para>
/// <para>
/// How strictly a key or a locale code is matched is the store's to decide, and the chain never
/// narrows what the store returned: it treats <c>EN</c> and <c>en</c> as one locale, so a store whose
/// collation is case insensitive resolves both and one whose collation is case sensitive resolves
/// whichever spelling its rows carry. A deployment on a case-sensitive collation therefore has to be
/// consistent about how it spells a locale code, and the shipped seed applier is: it treats a stored
/// row as the seed's row whatever the spelling, so seeding twice under two spellings inserts once.
/// </para>
/// </remarks>
public interface ITranslationReader
{
    /// <summary>
    /// Reads every row of one locale that the given tenant can see.
    /// </summary>
    /// <param name="locale">The locale code to read.</param>
    /// <param name="tenantId">The tenant to read for, or <see langword="null"/> for the global view.</param>
    /// <returns>The tenant's rows and the global rows of that locale, in no promised order.</returns>
    IReadOnlyList<Translation> ListForLocale(string locale, string? tenantId);

    /// <summary>
    /// Reads every row that could answer one key, in the requested locale or the fallback locale.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <param name="locale">The requested locale code.</param>
    /// <param name="fallbackLocale">The locale consulted when the requested one has no match.</param>
    /// <param name="tenantId">The tenant to read for, or <see langword="null"/> for the global view.</param>
    /// <returns>The candidate rows, in no promised order.</returns>
    /// <remarks>
    /// Both locales are fetched in one read on purpose: the chain would otherwise issue a second
    /// query for every key that is only translated in the fallback locale.
    /// </remarks>
    IReadOnlyList<Translation> ListForKey(string key, string locale, string fallbackLocale, string? tenantId);

    /// <summary>
    /// Reads every row of a locale and of its fallback locale that the given tenant can see.
    /// </summary>
    /// <param name="locale">The requested locale code.</param>
    /// <param name="fallbackLocale">The locale consulted when the requested one has no match.</param>
    /// <param name="tenantId">The tenant to read for, or <see langword="null"/> for the global view.</param>
    /// <returns>The candidate rows for the whole bundle, in no promised order.</returns>
    /// <remarks>
    /// This is the read behind the bundle endpoint. It deliberately does not narrow to a set of keys:
    /// the bundle is every key of the locale, and the chain resolves all of them against this one
    /// result.
    /// </remarks>
    IReadOnlyList<Translation> ListForLocaleOrFallback(string locale, string fallbackLocale, string? tenantId);

    /// <summary>
    /// Reads the distinct locale codes any stored row belongs to.
    /// </summary>
    /// <returns>The locale codes, deduplicated by the store, in no promised order.</returns>
    /// <remarks>
    /// Not narrowed by tenant, matching what the endpoint offers: the list of locales a deployment
    /// has content for is not tenant-specific.
    /// </remarks>
    IReadOnlyList<string> ListLocales();
}
