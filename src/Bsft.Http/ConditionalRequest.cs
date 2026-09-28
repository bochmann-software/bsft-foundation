using Microsoft.AspNetCore.Http;

namespace Bsft.Http;

/// <summary>
/// Reads the <c>If-None-Match</c> of a request against the tag of the representation it asked for.
/// </summary>
/// <remarks>
/// <para>
/// Only <c>If-None-Match</c> is honoured, deliberately. The endpoints using this serve
/// representations whose tag is a digest of their exact content, so a tag comparison answers "has
/// it changed" precisely; <c>If-Modified-Since</c> answers the same question to the nearest second
/// out of a clock the client and the server do not share, and supporting both means deciding which
/// wins when they disagree. The tag is the one that cannot be wrong.
/// </para>
/// <para>
/// Extension methods on <see cref="HttpRequest"/> because each is a pure reading of its first
/// argument, and it keeps the question reading as one asked of the request —
/// <c>request.IsUnchangedFrom(tag)</c>.
/// </para>
/// </remarks>
public static class ConditionalRequest
{
    /// <summary>The wildcard, which matches any representation that is there at all.</summary>
    private const string Wildcard = "*";

    /// <summary>The prefix marking a client's tag as a weak validator.</summary>
    private const string WeakPrefix = "W/";

    /// <summary>
    /// Reports whether the client already holds the representation this request would serve.
    /// </summary>
    /// <param name="request">The request whose <c>If-None-Match</c> to read.</param>
    /// <param name="entityTag">The tag of the representation about to be served, or <see langword="null"/> when it has none.</param>
    /// <returns><see langword="true"/> when the answer is 304 Not Modified.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// A representation with no tag is never unchanged: there is nothing to compare, and answering
    /// 304 on the strength of a client's assertion alone would let a stale cache pin itself for
    /// ever.
    /// </para>
    /// <para>
    /// Comparison is weak, as the specification requires for this header — a client's <c>W/"x"</c>
    /// matches a server's <c>"x"</c>. The answer is "you already have all of it", which is safe to
    /// give on a weak match in a way it would not be for a range request.
    /// </para>
    /// <para>
    /// A header this code cannot make sense of is <em>ignored</em> and the full representation is
    /// served. That is the only safe direction to fail in: a malformed validator that produced a
    /// 304 would hand a client a body it never received, while one that produces a 200 costs
    /// bandwidth and nothing else.
    /// </para>
    /// </remarks>
    public static bool IsUnchangedFrom(this HttpRequest request, string? entityTag)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        // Before the header is even materialised: a representation with no tag is never unchanged,
        // and building a string to reach that conclusion is work every such request would pay for.
        if (entityTag is null)
            return false;

        var ifNoneMatch = request.Headers.IfNoneMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
            return false;

        var served = Strip(entityTag);

        // The overwhelming case is one tag, and splitting it allocates an array plus a substring to
        // discover that. A list is walked only when there is a comma to walk over.
        if (!ifNoneMatch.Contains(',', StringComparison.Ordinal))
            return IsTheOneServed(ifNoneMatch, served);

        foreach (var candidate in ifNoneMatch.Split(','))
        {
            if (IsTheOneServed(candidate, served))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reports whether one tag a client offered is the one being served.
    /// </summary>
    /// <param name="candidate">One entry of the client's list, which may carry surrounding space.</param>
    /// <param name="served">The bare body of the tag about to be served.</param>
    /// <returns><see langword="true"/> when the client already holds this representation.</returns>
    private static bool IsTheOneServed(string candidate, string served)
    {
        var trimmed = candidate.Trim();

        return string.Equals(trimmed, Wildcard, StringComparison.Ordinal)
            || string.Equals(Strip(trimmed), served, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reduces a tag to the opaque text inside it, so that two spellings of the same tag compare equal.
    /// </summary>
    /// <param name="tag">The tag as it was written, quoted or not, weak or not.</param>
    /// <returns>The bare tag body.</returns>
    private static string Strip(string tag)
    {
        var bare = tag.StartsWith(WeakPrefix, StringComparison.Ordinal) ? tag[WeakPrefix.Length..] : tag;

        return bare.Trim('"');
    }
}
