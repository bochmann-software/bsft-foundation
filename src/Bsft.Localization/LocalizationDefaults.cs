namespace Bsft.Localization;

/// <summary>
/// The constants a host may need to name the localization surface without duplicating a literal.
/// </summary>
public static class LocalizationDefaults
{
    /// <summary>
    /// The default path the translation endpoints are mapped under, <c>/api/localization</c>.
    /// </summary>
    public const string RoutePrefix = "/api/localization";

    /// <summary>
    /// The default locale, <c>en</c>: the one assumed when a request names none, and the one consulted
    /// when the requested locale has no match.
    /// </summary>
    public const string Locale = "en";
}
