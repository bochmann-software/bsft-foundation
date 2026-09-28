using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Bsft.Http;

/// <summary>
/// Reduces the content of a response to one SHA-256 digest that is the same on every host, in
/// every process and on every run for the same content, and different for different content.
/// </summary>
/// <remarks>
/// <para>
/// An entity tag is only worth anything if it is a function of the content alone. A digest taken
/// over a serializer's output is not: property order, culture-dependent number formatting and
/// whitespace all move it without the content moving, so two servers behind one load balancer
/// would hand out two tags for the same body and every client would revalidate for ever. This
/// hashes a caller-supplied sequence of segments instead, so the order is fixed by the caller's
/// code rather than by whatever a serializer decided today.
/// </para>
/// <para>
/// Each segment is length-prefixed before it is hashed. Concatenating <c>"ab"</c> and <c>"c"</c>
/// otherwise produces the same bytes as <c>"a"</c> and <c>"bc"</c>, which is how two different
/// contents come to share a tag and one client ends up pinned to the other's body. The same
/// length-prefixing that <c>Bsft.Caching</c>'s key builder uses, and for the same reason.
/// </para>
/// <para>
/// SHA-256 rather than a fast non-cryptographic hash: a tag is served to callers who may choose
/// what goes into it, and a hash they can find collisions in is a hash they can use to make the
/// server answer 304 for content the caller never saw.
/// </para>
/// <para>
/// An extension method rather than a static helper taking one argument: it is a pure transform of
/// the sequence it is given, and <c>segments.ToCanonicalHash()</c> reads as the digest of those
/// segments rather than as a call to a utility.
/// </para>
/// </remarks>
public static class CanonicalHash
{
    /// <summary>The marker standing in for a segment that is absent rather than empty.</summary>
    /// <remarks>
    /// Encoded once. A length no real segment can have, so an absent segment and an empty one are
    /// different inputs rather than the same one.
    /// </remarks>
    private static readonly byte[] NullLength = "-1"u8.ToArray();

    /// <summary>
    /// Room for the longest length prefix there can be, which is <see cref="int.MaxValue"/> at ten
    /// digits.
    /// </summary>
    private const int LongestLength = 10;

    /// <summary>Separates a segment's length from its bytes.</summary>
    private const byte LengthTerminator = (byte)':';

    /// <summary>
    /// Hashes a sequence of segments into a lowercase hexadecimal SHA-256 digest.
    /// </summary>
    /// <param name="segments">
    /// The content, already reduced to strings in a fixed order. A <see langword="null"/> segment
    /// is distinguished from an empty one.
    /// </param>
    /// <returns>The digest as 64 lowercase hexadecimal characters.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="segments"/> is null.</exception>
    public static string ToCanonicalHash(this IEnumerable<string?> segments)
    {
        if (segments is null)
            throw new ArgumentNullException(nameof(segments));

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var segment in segments)
        {
            Append(digest, segment);
        }

        return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// Feeds one length-prefixed segment into the running digest.
    /// </summary>
    /// <param name="digest">The running digest.</param>
    /// <param name="segment">The segment, which may be absent.</param>
    private static void Append(IncrementalHash digest, string? segment)
    {
        if (segment is null)
        {
            digest.AppendData(NullLength);
            digest.AppendData([LengthTerminator]);

            return;
        }

        var bytes = Encoding.UTF8.GetBytes(segment);

        // Formatted into the stack rather than through a string and a second array. This runs once
        // per segment of every tag an endpoint builds, and the digest that follows is the only part
        // of it that has to cost anything.
        Span<byte> length = stackalloc byte[LongestLength];
        Utf8Formatter.TryFormat(bytes.Length, length, out var written);

        digest.AppendData(length[..written]);
        digest.AppendData([LengthTerminator]);
        digest.AppendData(bytes);
    }
}
