namespace Bsft.Localization;

/// <summary>
/// Holds the configured locale settings and endpoint bounds for the Bsft localization system.
/// </summary>
public sealed class BsftLocalizationOptions
{
    /// <summary>
    /// The configuration section this options type binds from, <c>Bsft:Localization</c>.
    /// </summary>
    public const string SectionName = "Bsft:Localization";

    /// <summary>
    /// Gets or sets the default locale used when no culture is explicitly specified.
    /// </summary>
    public string DefaultLocale { get; set; } = LocalizationDefaults.Locale;

    /// <summary>
    /// Gets or sets the fallback locale tried when the primary locale has no match.
    /// </summary>
    public string FallbackLocale { get; set; } = LocalizationDefaults.Locale;

    /// <summary>
    /// Gets or sets the path the translation endpoints are mapped under. Defaults to
    /// <see cref="LocalizationDefaults.RoutePrefix"/>.
    /// </summary>
    /// <remarks>
    /// Read once, when <c>MapBsftLocalization</c> runs, so a change after start has no effect: a
    /// route table is built at start.
    /// </remarks>
    public string RoutePrefix { get; set; } = LocalizationDefaults.RoutePrefix;

    /// <summary>
    /// Gets or sets how many distinct modules the bundle endpoint accepts in one
    /// <c>?modules=</c> filter. Defaults to 32.
    /// </summary>
    /// <remarks>
    /// The filter is caller-supplied and decides how much narrowing one request performs, so it
    /// needs a ceiling for the same reason a page size does: without one, the caller rather than the
    /// host decides what a request costs.
    /// </remarks>
    public int MaxFilterModules { get; set; } = 32;

    /// <summary>
    /// Gets or sets the greatest length of a single module name in a <c>?modules=</c> filter.
    /// Defaults to 64 characters.
    /// </summary>
    public int MaxFilterModuleNameLength { get; set; } = 64;

    /// <summary>
    /// Validates the options.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when a value cannot be used.</exception>
    /// <remarks>
    /// Internal because it states the rules rather than being a service: <c>AddBsftLocalization</c>
    /// runs it before anything can resolve the options, and
    /// <see cref="BsftLocalizationOptionsValidator"/> runs it again through the options
    /// infrastructure on start.
    /// </remarks>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(DefaultLocale))
            throw new InvalidOperationException($"{nameof(DefaultLocale)} must be a locale code, but it is blank.");

        if (string.IsNullOrWhiteSpace(FallbackLocale))
            throw new InvalidOperationException($"{nameof(FallbackLocale)} must be a locale code, but it is blank.");

        if (string.IsNullOrWhiteSpace(RoutePrefix) || !RoutePrefix.StartsWith('/'))
        {
            throw new InvalidOperationException(
                $"{nameof(RoutePrefix)} must be an absolute path starting with '/', but it is '{RoutePrefix}'.");
        }

        if (MaxFilterModules <= 0)
            throw new InvalidOperationException($"{nameof(MaxFilterModules)} must be positive, but it is {MaxFilterModules}.");

        if (MaxFilterModuleNameLength <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxFilterModuleNameLength)} must be positive, but it is {MaxFilterModuleNameLength}.");
        }
    }
}
