namespace Bsft.Localization;

/// <summary>
/// What the bundle endpoints read: the locales on offer, and the whole bundle of one of them.
/// </summary>
/// <remarks>
/// The endpoints hold this rather than <see cref="Microsoft.Extensions.Localization.IStringLocalizerFactory"/>.
/// What they need is not a localizer but the two whole-bundle reads behind one, and asking for the
/// factory meant casting its answer back to the implementation this package happens to register — a
/// seam that a host bringing its own factory broke silently, into an empty response rather than an
/// error. Reading through a service of its own also keeps the endpoints working for such a host,
/// because this is registered by <c>AddBsftLocalization</c> whatever else the host replaces.
/// </remarks>
internal interface ITranslationBundleReader
{
    /// <summary>Returns every locale code found across the store and the seeds.</summary>
    /// <returns>The sorted, deduplicated locale codes.</returns>
    IReadOnlyList<string> ListAvailableLocales();

    /// <summary>Resolves every key of a locale for the tenant of the current request.</summary>
    /// <param name="locale">The locale to resolve translations for.</param>
    /// <returns>The resolved bundle and the version that identifies it.</returns>
    TranslationBundle ResolveBundle(string locale);
}
