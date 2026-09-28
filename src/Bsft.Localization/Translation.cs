namespace Bsft.Localization;

/// <summary>
/// One stored translation, as the resolution chain reads it.
/// </summary>
/// <param name="Key">The translation key the value is looked up under.</param>
/// <param name="Locale">The locale code the value belongs to, an IETF tag such as <c>en</c> or <c>de-CH</c>.</param>
/// <param name="Value">The translated text.</param>
/// <param name="TenantId">The tenant the row belongs to, or <see langword="null"/> for a global row.</param>
/// <remarks>
/// The store's own row type stays in the package that owns the schema. This is the shape the
/// localizer resolves against, so that the fallback chain — which is the part worth keeping in one
/// place — does not depend on how the rows are stored or on which ORM stores them.
/// </remarks>
public sealed record Translation(string Key, string Locale, string Value, string? TenantId);
