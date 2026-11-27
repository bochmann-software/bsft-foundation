
namespace Bsft.Core;

/// <summary>The one canonical form of an internet domain name, shared by every path that compares, stores or looks one up.</summary>
/// <remarks>
/// <para>
/// A name is processed as UTS 46 (Unicode IDNA Compatibility Processing) prescribes for registration: non-transitional
/// processing, with the hyphen, joiner, bidirectional and STD3 ASCII rules on and with DNS length verification. The
/// canonical form is the ASCII form (A-label per label, lower case), so <c>müller.de</c>, <c>MÜLLER.DE</c>,
/// <c>mueller.de</c> written with a full-width dot and <c>xn--mller-kva.de</c> are one name, and anything that cannot
/// be a registrable name is refused instead of being repaired.
/// </para>
/// <para>
/// The processing reads the mapping table embedded in this assembly and implements Punycode itself. It does not ask
/// <see cref="System.Globalization.IdnMapping"/>, which answers from ICU on Linux and from the operating system on
/// Windows: the two know different Unicode versions and differ at the edges (the deviation characters, joiners,
/// bidirectional labels), and one name would canonicalize to different values on two hosts sharing a database.
/// Unicode normalization form C comes from <see cref="UnicodeNormalization"/>, which reads tables of the same Unicode
/// version, so the result is the same under invariant globalization, with an old ICU and on Windows.
/// </para>
/// <para>
/// Beyond UTS 46 the name needs at least two labels, no trailing dot and a last label that is not all digits (an IP
/// address is not a domain), and is at most <see cref="MaximumLength"/> characters in its ASCII form.
/// </para>
/// </remarks>
public static class InternetDomain
{
    /// <summary>The longest domain accepted, in its ASCII form (RFC 1035).</summary>
    public const int MaximumLength = 253;

    /// <summary>Converts a name to its canonical ASCII form, when it is a registrable name.</summary>
    /// <param name="domain">The name as the caller gave it; surrounding whitespace is trimmed.</param>
    /// <param name="canonical">The canonical form when this returns <see langword="true"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the name is valid.</returns>
    public static bool TryCanonicalizeDomain(this string? domain, out string? canonical)
    {
        canonical = null;

        var trimmed = domain?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaximumLength || !IdnaMapper.TryMap(trimmed, out var mapped))
            return false;

        var labels = mapped.Split('.');
        var unicode = new string[labels.Length];
        var ascii = new string[labels.Length];

        for (var i = 0; i < labels.Length; i++)
        {
            if (!IdnaLabel.TryConvert(labels[i], out unicode[i], out ascii[i]))
                return false;
        }

        var result = string.Join('.', ascii);

        if (labels.Length < 2 || result.Length > MaximumLength || ascii[^1].All(char.IsAsciiDigit) || !IdnaBidi.IsSatisfied(unicode))
            return false;

        canonical = result;

        return true;
    }

    /// <summary>Converts a canonical name to its Unicode form, for display and for the look-alike skeleton.</summary>
    /// <param name="canonical">A name that <see cref="TryCanonicalizeDomain"/> returned.</param>
    /// <returns>The name with every A-label decoded; a label that does not decode is kept as it is.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="canonical"/> is <see langword="null"/>.</exception>
    public static string ToUnicodeDomain(this string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);

        return string.Join('.', canonical.Split('.').Select(DecodeLabel));
    }

    private static string DecodeLabel(string label)
        => label.StartsWith(IdnaLabel.AcePrefix, StringComparison.Ordinal) && PunycodeDecoder.TryDecode(label[IdnaLabel.AcePrefix.Length..], out var decoded)
            ? decoded
            : label;
}
