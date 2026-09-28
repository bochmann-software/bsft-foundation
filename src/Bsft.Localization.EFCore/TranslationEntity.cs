namespace Bsft.Localization.EFCore;

/// <summary>
/// Represents a single translation entry stored in the database.
/// </summary>
public sealed class TranslationEntity
{
    /// <summary>
    /// Gets or sets the unique identifier of the translation.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the translation key used to look up the value.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the locale code (e.g. "en", "de") this translation belongs to.
    /// </summary>
    public string Locale { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the translated text.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tenant identifier, or <c>null</c> for global translations.
    /// </summary>
    public string? TenantId { get; set; }
}
