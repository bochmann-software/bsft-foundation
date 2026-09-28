using System.Globalization;

namespace Bsft.Localization.Bundles;

/// <summary>
/// What the bundle endpoint accepts as the locale in its route.
/// </summary>
/// <remarks>
/// <para>
/// The segment reaches a memory-cache key and a bundle that is held for half an hour, so what a
/// caller may put there decides how many bundles an anonymous request can make the process hold.
/// Without this the endpoint takes any string at all, and a caller asking for a thousand spellings
/// of nothing leaves a thousand copies of the fallback bundle behind.
/// </para>
/// <para>
/// Two checks, because they bound different things. The shape bounds the length; asking the runtime
/// whether it knows the tag bounds the count, to the language tags ICU actually carries. A host
/// running with invariant globalization has no such list — every well-shaped tag is a culture there
/// — so it keeps the shape bound and nothing more, which is the trade that mode makes everywhere
/// else too.
/// </para>
/// </remarks>
internal static class LocaleTag
{
    /// <summary>The longest tag accepted, which is past every tag in ordinary use.</summary>
    private const int MaxLength = 35;

    /// <summary>The longest single subtag, per the grammar of RFC 5646.</summary>
    private const int MaxSubtagLength = 8;

    /// <summary>What separates the subtags of a language tag.</summary>
    private const char SubtagSeparator = '-';

    /// <summary>
    /// Reports whether a route segment names a locale this endpoint will resolve.
    /// </summary>
    /// <param name="locale">The segment as it arrived.</param>
    /// <returns><see langword="true"/> when it is a language tag the runtime knows.</returns>
    internal static bool IsAcceptable(string? locale)
        => HasTagShape(locale) && IsKnownToTheRuntime(locale!);

    /// <summary>
    /// Reports whether a string is shaped like a language tag: alphanumeric subtags of at most eight
    /// characters, separated by single hyphens.
    /// </summary>
    /// <param name="locale">The string to judge.</param>
    /// <returns><see langword="true"/> when it has the shape.</returns>
    private static bool HasTagShape(string? locale)
    {
        if (string.IsNullOrEmpty(locale) || locale.Length > MaxLength)
            return false;

        var subtagLength = 0;

        foreach (var character in locale)
        {
            if (character == SubtagSeparator)
            {
                if (subtagLength == 0)
                    return false;

                subtagLength = 0;
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(character) || ++subtagLength > MaxSubtagLength)
                return false;
        }

        return subtagLength > 0;
    }

    /// <summary>
    /// Reports whether the runtime carries a culture under this name.
    /// </summary>
    /// <param name="locale">The tag to look up.</param>
    /// <returns><see langword="true"/> when the runtime knows it.</returns>
    /// <remarks>
    /// Looked up rather than constructed, so the answer is cached by the runtime and a repeated
    /// request costs a dictionary lookup. <c>predefinedOnly</c> is what makes this a bound: without
    /// it the runtime happily manufactures a culture for any well-formed tag it has never heard of.
    /// </remarks>
    private static bool IsKnownToTheRuntime(string locale)
    {
        try
        {
            CultureInfo.GetCultureInfo(locale, predefinedOnly: true);

            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
