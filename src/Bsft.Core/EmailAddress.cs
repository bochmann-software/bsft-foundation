namespace Bsft.Core;

/// <summary>The one canonical form of an e-mail address, for every path that stores, compares, looks up or delivers one.</summary>
/// <remarks>
/// <para>
/// An address is proven by a link, claimed by an invitation and looked up at a login, and the mailer delivers to whatever
/// it parses. Anything two of them read differently is an address that is announced as verified and never reached: a
/// display name or comment, a trailing dot, an invisible or compatibility character, a look-alike letter from another
/// script, a full-width <c>@</c>. The canonical form is therefore narrow: a dot-atom local part of at most 64 ASCII
/// characters, one <c>@</c>, and a domain that <see cref="InternetDomain"/> accepts, 254 characters in all.
/// </para>
/// <para>
/// What is stored and delivered is the address as entered, with the domain in its canonical A-label form: the local
/// part keeps its exact characters and case (RFC 5321 leaves its case to the receiving system), the domain is lower
/// case ASCII (<c>müller.de</c> becomes <c>xn--mller-kva.de</c>). Uniqueness and lookup are case-insensitive and use
/// <see cref="TryFold"/>, so <c>Ada@Müller.DE</c> and <c>ada@xn--mller-kva.de</c> are one account.
/// </para>
/// <para>
/// An internationalized local part (RFC 6531) is refused: delivering to it needs SMTPUTF8 on every hop, and an address
/// that cannot be delivered cannot be verified.
/// </para>
/// </remarks>
public static class EmailAddress
{
    /// <summary>The longest address accepted (the RFC 5321 path limit), which also keeps the claim inside the token's bound.</summary>
    public const int MaximumLength = 254;

    private const int MaximumLocalLength = 64;
    private const string AtomSpecials = "!#$%&'*+-/=?^_`{|}~";

    /// <summary>Returns the address in its canonical form, when it is one.</summary>
    /// <param name="address">The address as the caller gave it; surrounding whitespace is trimmed.</param>
    /// <param name="canonical">The local part as entered, <c>@</c> and the canonical domain when this returns <see langword="true"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the address is a plain addr-spec with an ASCII local part.</returns>
    public static bool TryCanonicalize(this string? address, out string? canonical)
    {
        canonical = null;

        var trimmed = address?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaximumLength)
            return false;

        var at = trimmed.IndexOf('@', StringComparison.Ordinal);

        if (at < 1 || at != trimmed.LastIndexOf('@') || !IsLocalPart(trimmed.AsSpan(0, at)) || !trimmed[(at + 1)..].TryCanonicalizeDomain(out var domain))
            return false;

        var result = string.Concat(trimmed.AsSpan(0, at + 1), domain);

        if (result.Length > MaximumLength)
            return false;

        canonical = result;

        return true;
    }

    /// <summary>Returns the key two spellings of one address share, when the address is canonical.</summary>
    /// <param name="address">The address as the caller gave it.</param>
    /// <param name="folded">The canonical form, upper-cased invariantly, when this returns <see langword="true"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the address is canonicalizable.</returns>
    /// <remarks>
    /// Upper-cased rather than lower-cased, which is what ASP.NET Core Identity does and what the collations of the
    /// databases we ship on agree on. The local part is case-sensitive per RFC 5321 and folded anyway: no mail system in
    /// practice treats two addresses differing only in case as two people, and treating them as two would let an attacker
    /// register the second one. Both parts are ASCII, so the fold does not depend on a culture or on the Unicode version.
    /// </remarks>
    public static bool TryFold(this string? address, out string? folded)
    {
        folded = address.TryCanonicalize(out var canonical) ? canonical!.ToUpperInvariant() : null;

        return folded is not null;
    }

    private static bool IsLocalPart(ReadOnlySpan<char> local)
    {
        if (local.Length > MaximumLocalLength || local[0] == '.' || local[^1] == '.')
            return false;

        for (var i = 0; i < local.Length; i++)
        {
            var c = local[i];

            if (c == '.' ? local[i - 1] == '.' : !IsAtom(c))
                return false;
        }

        return true;
    }

    private static bool IsAtom(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') || AtomSpecials.Contains(c, StringComparison.Ordinal);
}
