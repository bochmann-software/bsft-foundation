namespace Bsft.Localization;

/// <summary>
/// A resolved translation bundle together with the version that identifies it.
/// </summary>
/// <param name="Translations">
/// Every key of the locale with the value the fallback chain resolved it to, for one tenant.
/// </param>
/// <param name="Version">
/// The SHA-256 of the bundle's content, as hexadecimal. Computed once, when the bundle is
/// materialised, and carried in the same cache entry as the content it describes — so the version
/// cannot outlive the content or lag behind it, whatever drops the entry.
/// </param>
internal sealed record TranslationBundle(IReadOnlyDictionary<string, string> Translations, string Version);
