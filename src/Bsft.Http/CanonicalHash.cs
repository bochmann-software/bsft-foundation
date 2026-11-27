using System.ComponentModel;

namespace Bsft.Http;

/// <summary>
/// The entry point the canonical content hash had in <c>Bsft.Http</c> before it moved to
/// <see cref="Bsft.Core.CanonicalHash"/>.
/// </summary>
/// <remarks>
/// <para>
/// The hash moved to <c>Bsft.Core</c> so that code without an ASP.NET Core reference can compute the same
/// digest an entity tag is made of. A type forward cannot carry the move, because a forward keeps the full
/// type name and the namespace of a <c>Bsft.Core</c> type is <c>Bsft.Core</c>. This class keeps the method an
/// assembly compiled against an older <c>Bsft.Http</c> calls, with the same signature, and delegates to the
/// one implementation, so a binary built against either assembly computes the same digest.
/// </para>
/// <para>
/// It is deliberately no longer an extension method: a file importing both <c>Bsft.Core</c> and
/// <c>Bsft.Http</c> would otherwise see two equally good <c>ToCanonicalHash</c> extensions and refuse to
/// compile. <c>segments.ToCanonicalHash()</c> binds to <see cref="Bsft.Core.CanonicalHash"/> once
/// <c>Bsft.Core</c> is imported, which every reference to <c>Bsft.Http</c> brings along.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CanonicalHash
{
    /// <summary>
    /// Hashes a sequence of segments into a lowercase hexadecimal SHA-256 digest, exactly as
    /// <see cref="Bsft.Core.CanonicalHash.ToCanonicalHash"/> does.
    /// </summary>
    /// <param name="segments">The content, already reduced to strings in a fixed order.</param>
    /// <returns>The digest as 64 lowercase hexadecimal characters.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="segments"/> is null.</exception>
    public static string ToCanonicalHash(IEnumerable<string?> segments) =>
        Bsft.Core.CanonicalHash.ToCanonicalHash(segments);
}
