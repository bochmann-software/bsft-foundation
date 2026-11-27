using System.Globalization;

namespace Bsft.Localization;

/// <summary>
/// Resolves one translation for a tenant and a culture the caller names, through the same fallback
/// chain the string localizer uses.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer"/> answers for the culture of the
/// thread and the tenant of the request. This is for the caller that knows both and must not depend
/// on either: a render for a recipient whose language is not the language of whoever triggered it,
/// or one that runs outside a request.
/// </para>
/// <para>
/// The lookup resolves through the chain the string localizer shares with it when both come from
/// <c>AddBsftLocalization</c>. A hit is read from and written to the cache the localizer shares. A miss
/// is remembered in a cache of its own, bounded in number and in lifetime
/// (<see cref="BsftLocalizationOptions.MissCacheMaxEntries"/>,
/// <see cref="BsftLocalizationOptions.MissCacheLifetime"/>), so a key that is not there costs a store
/// read once per lifetime rather than once per call, and a key added later appears within that
/// lifetime or when <see cref="TranslationsUpdatedEvent"/> is published, which clears the misses.
/// Remembering the result of one call across the lookups of one render is the caller's job.
/// </para>
/// <para>
/// A culture whose locale the host did not configure (not the default, not the fallback, not one a
/// seed holds) is resolved but not cached, hit or miss, so a caller cannot grow the caches by naming
/// cultures. Keys longer than <see cref="BsftLocalizationOptions.MaxKeyLength"/> are refused.
/// </para>
/// <para>
/// The chain matches keys and locales ignoring case, but the hit cache compares them exactly, because
/// the store may be case sensitive. A hit is therefore cached only when the requested key and locale
/// equal, ordinally, the spelling of the row or seed entry that matched (a fallback match needs a
/// configured spelling of the requested locale); a request in another spelling gets the same answer
/// the localizer gives but is read from the store again. Misses go to the bounded miss cache as before.
/// </para>
/// <para>
/// Each tenant id is a dimension of both caches, so <c>tenantId</c> must be the tenant the host
/// resolved (for example from <c>ITenantContext</c>) and never a value taken from template or request
/// input: a caller that can name tenants can make the caches hold one entry per name it invents.
/// </para>
/// <para>
/// The read is synchronous, like <see cref="ITranslationReader"/>: a key that is not cached is a store
/// read on the calling thread.
/// </para>
/// </remarks>
public interface ITranslationLookup
{
    /// <summary>
    /// Resolves a key for the given tenant and culture.
    /// </summary>
    /// <param name="tenantId">The tenant to resolve for, or <see langword="null"/> for the global translations only.</param>
    /// <param name="culture">The culture to resolve for; its name is the locale code. The invariant culture resolves under the default locale.</param>
    /// <param name="key">The translation key.</param>
    /// <returns>The resolved value, or <see langword="null"/> when no level of the chain has the key.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null, empty or longer than <see cref="BsftLocalizationOptions.MaxKeyLength"/>.</exception>
    string? Find(string? tenantId, CultureInfo culture, string key);
}
