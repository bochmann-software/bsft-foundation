using Microsoft.AspNetCore.Http;

namespace Bsft.Idempotency.Fingerprinting;

/// <summary>
/// Adjusts the segment sequence the request fingerprint is hashed from, before it is hashed.
/// </summary>
/// <remarks>
/// <para>
/// It takes the sequence and returns one, rather than merely appending, so a host can remove a
/// segment as well as add one — a proxy that rewrites the content type, an API surface whose
/// version travels in a header rather than in the route.
/// </para>
/// <para>
/// The ordering rule is the same one <c>Bsft.Http</c>'s <c>CanonicalHash</c> documents and cannot
/// enforce: the sequence is positional, so a contributor that inserts a segment in the middle
/// changes every fingerprint the application has already stored. Append, or replace a segment in
/// place; and sort anything whose enumeration order is not a promise, or two replicas will hash one
/// request two ways.
/// </para>
/// </remarks>
public interface IIdempotencyFingerprintContributor
{
    /// <summary>
    /// Returns the segment sequence to hash.
    /// </summary>
    /// <param name="request">The request being fingerprinted.</param>
    /// <param name="segments">
    /// The segments as they stand: method, matched route pattern, sorted query, tenant, subject,
    /// client id, content type, and the digest of the body.
    /// </param>
    /// <returns>The sequence to hash, which may be <paramref name="segments"/> unchanged.</returns>
    IReadOnlyList<string?> Contribute(HttpRequest request, IReadOnlyList<string?> segments);
}
