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

    /// <summary>
    /// The default longest key, in characters, the culture-explicit lookup accepts: 256.
    /// </summary>
    public const int MaxKeyLength = 256;

    /// <summary>
    /// The default number of misses the culture-explicit lookup remembers at once: 10 000.
    /// </summary>
    public const int MissCacheMaxEntries = 10_000;

    /// <summary>
    /// The default time, in seconds, the culture-explicit lookup remembers a miss: 30.
    /// </summary>
    public const int MissCacheLifetimeSeconds = 30;
}
