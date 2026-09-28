using Microsoft.AspNetCore.Http;

namespace Bsft.Http;

/// <summary>
/// Writes the validator and caching headers of a response a client is meant to revalidate rather
/// than to reuse blindly.
/// </summary>
/// <remarks>
/// <para>
/// The two headers belong together and are therefore written together. An <c>ETag</c> without a
/// caching policy is served under whatever default the host, a reverse proxy or a CDN happens to
/// apply; a caching policy without an <c>ETag</c> makes every revalidation a full download. Two
/// endpoints spelling that pairing separately is how one of them ends up cacheable by a proxy that
/// serves one tenant's response to the next.
/// </para>
/// </remarks>
public static class ConditionalResponse
{
    /// <summary>
    /// The caching policy of a representation that is specific to its caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>private</c> keeps a shared cache — a reverse proxy, a CDN, a corporate middlebox — from
    /// storing the response at all. That is the part that matters: these representations depend on
    /// the authenticated principal and the tenant of the request, neither of which is in the URL,
    /// so a shared cache keyed on the URL would serve one caller's answer to the next.
    /// </para>
    /// <para>
    /// <c>no-cache</c> does not mean "do not store", which is the reading its name invites. It
    /// means the client's own copy may be kept but must be revalidated before it is used again.
    /// That is exactly the bargain this pairing is for: the client keeps the body, and the
    /// round-trip that follows is a conditional request the server answers with an empty 304
    /// whenever nothing changed.
    /// </para>
    /// <para>
    /// <c>no-store</c> would be the stricter choice and is the wrong one here. It would forbid the
    /// client from keeping the body, which turns every revalidation back into a full download and
    /// throws away the point of the entity tag. Neither representation is a secret in the sense
    /// <c>no-store</c> protects: both are already rendered into the client that asked for them.
    /// </para>
    /// </remarks>
    public const string PrivateRevalidate = "private, no-cache";

    /// <summary>
    /// Puts the entity tag and the revalidation policy on a response.
    /// </summary>
    /// <param name="response">The response to write the headers on.</param>
    /// <param name="entityTag">The strong entity tag of the representation, as built by <see cref="EntityTag"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="entityTag"/> is null or blank.</exception>
    /// <remarks>
    /// Called on the 304 as well as on the 200. A 304 stands in for the response it replaces, so it
    /// has to carry the headers that response would have carried; a 304 without an <c>ETag</c>
    /// leaves the client unable to tell which of its stored representations was just confirmed.
    /// </remarks>
    public static void UseRevalidation(this HttpResponse response, string entityTag)
    {
        if (response is null)
            throw new ArgumentNullException(nameof(response));

        if (string.IsNullOrWhiteSpace(entityTag))
            throw new ArgumentException("A response cannot be revalidated against a blank entity tag.", nameof(entityTag));

        response.Headers.ETag = entityTag;
        response.Headers.CacheControl = PrivateRevalidate;
    }
}
