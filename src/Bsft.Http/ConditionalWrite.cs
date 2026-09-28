namespace Bsft.Http;

/// <summary>
/// Reads the <c>If-Match</c> a conditional write carries against the tag of the resource as it
/// actually stands.
/// </summary>
/// <remarks>
/// <para>
/// The write-side counterpart of <see cref="ConditionalRequest"/>, and deliberately not part of it:
/// the two headers are compared under different rules and failing in opposite directions.
/// <c>If-None-Match</c> is compared weakly and a header that cannot be understood is ignored,
/// because the worst outcome there is a body served that need not have been. <c>If-Match</c> is
/// compared strongly and anything that is not an exact match refuses the write, because the worst
/// outcome here is an overwrite of a change the writer never saw.
/// </para>
/// <para>
/// It takes the raw header value rather than the request, so the comparison can sit wherever the
/// resource's current version is known. In this framework's shape that is the command handler,
/// which reads the row and decides; the endpoint's job is only to insist the header was sent at
/// all and to carry it into the command.
/// </para>
/// </remarks>
public static class ConditionalWrite
{
    /// <summary>The wildcard, which matches any version the resource currently has.</summary>
    private const string Wildcard = "*";

    /// <summary>
    /// Reports whether an <c>If-Match</c> the caller sent still names the resource's current version.
    /// </summary>
    /// <param name="currentEntityTag">The strong tag the resource has now, as built by <see cref="EntityTag"/>.</param>
    /// <param name="ifMatch">The raw <c>If-Match</c> header value, which may be a list, the wildcard, absent or blank.</param>
    /// <returns><see langword="true"/> when the precondition holds and the write may proceed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="currentEntityTag"/> is null or blank.</exception>
    /// <remarks>
    /// <para>
    /// Comparison is strong, as RFC 9110 requires for this header: the wildcard matches any version
    /// the resource currently has, and otherwise the caller's tag has to equal the current one
    /// exactly. A weak validator therefore never satisfies it, which is the point — a write is gated
    /// on the exact version the client last saw, not on one that is merely equivalent for caching.
    /// </para>
    /// <para>
    /// An absent or blank header answers <see langword="false"/> rather than throwing, so the only
    /// way past this check is a header that matches. An endpoint that wants the caller told it
    /// forgot the header — a <c>428</c> rather than a <c>412</c> — asks that question before the
    /// write reaches here, where it still knows nothing has been read yet.
    /// </para>
    /// <para>
    /// The list is split on commas, which the tags this framework builds never contain. A tag body
    /// that did contain one would be split into two candidates and match neither, which fails the
    /// write closed; the same reading <see cref="ConditionalRequest"/> takes, and the harmless
    /// direction for both.
    /// </para>
    /// </remarks>
    public static bool IsMatchedBy(this string currentEntityTag, string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(currentEntityTag))
            throw new ArgumentException(
                "A write cannot be matched against a blank entity tag.",
                nameof(currentEntityTag));

        if (string.IsNullOrWhiteSpace(ifMatch))
            return false;

        // The overwhelming case is one tag, and splitting it allocates an array plus a substring to
        // discover that. A list is walked only when there is a comma to walk over, the same reading
        // ConditionalRequest.IsUnchangedFrom takes.
        if (!ifMatch.Contains(',', StringComparison.Ordinal))
            return IsMatch(ifMatch, currentEntityTag);

        foreach (var candidate in ifMatch.Split(','))
        {
            if (IsMatch(candidate, currentEntityTag))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reports whether one candidate from the header names the current version.
    /// </summary>
    /// <param name="candidate">One entry of the header, with whatever spacing it arrived with.</param>
    /// <param name="currentEntityTag">The tag the resource has now.</param>
    /// <returns><see langword="true"/> when the candidate satisfies the precondition.</returns>
    private static bool IsMatch(string candidate, string currentEntityTag)
    {
        var trimmed = candidate.Trim();

        return string.Equals(trimmed, Wildcard, StringComparison.Ordinal)
            || string.Equals(trimmed, currentEntityTag, StringComparison.Ordinal);
    }
}
