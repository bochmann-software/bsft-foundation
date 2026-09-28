using System.Text;

namespace Bsft.Http;

/// <summary>
/// Builds the strong entity tags the framework's endpoints put on <c>ETag</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every tag this produces is a <em>strong</em> validator, quoted, and safe to put in a header. It
/// is the same shape <c>Bsft.Storage.Abstractions.StorageEntityTags</c> serves for downloads —
/// <c>"sha256-&lt;hex&gt;"</c> — so a client that already knows how to revalidate a stored file
/// needs to learn nothing new to revalidate a translation bundle or a permission map.
/// </para>
/// <para>
/// Two shapes are served, and both end up in the same grammar. A representation the server
/// assembles per request is tagged by the digest of its content
/// (<see cref="ToContentHashEntityTag"/>); a resource that carries a concurrency token of its own
/// is tagged by that token (<see cref="ToVersionEntityTag(string)"/>). The size-and-timestamp
/// fallback a file store needs has no counterpart for either: a computed response has no
/// last-write time of its own, and inventing one would produce a tag that changes when nothing
/// did.
/// </para>
/// <para>
/// An extension method rather than a static helper taking one argument, for the same reason as its
/// storage counterpart: it is a pure transform of its first argument, and the tag reads as a
/// property of the digest it stands for.
/// </para>
/// </remarks>
public static class EntityTag
{
    /// <summary>The prefix marking a tag as the SHA-256 of the representation it stands for.</summary>
    public const string ContentHashPrefix = "sha256-";

    /// <summary>
    /// Builds the tag for a representation whose digest is known.
    /// </summary>
    /// <param name="sha256">The SHA-256 of the representation, as hexadecimal.</param>
    /// <returns>A quoted strong entity tag of the form <c>"sha256-&lt;hex&gt;"</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sha256"/> is null or blank.</exception>
    /// <remarks>
    /// The digest is sanitized even though every caller in the framework hands over hexadecimal it
    /// generated itself. The cost is one pass over 64 characters; the alternative is that the day
    /// someone builds a tag out of a value with a carriage return in it, the response grows a
    /// header nobody wrote.
    /// </remarks>
    public static string ToContentHashEntityTag(this string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256))
            throw new ArgumentException("A content hash is required to build an entity tag from one.", nameof(sha256));

        return $"\"{ContentHashPrefix}{Sanitize(sha256)}\"";
    }

    /// <summary>
    /// Builds the tag standing for a resource at a given version.
    /// </summary>
    /// <param name="version">The resource's concurrency token, in whatever spelling it is stored.</param>
    /// <returns>A quoted strong entity tag of the form <c>"sha256-&lt;hex&gt;"</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="version"/> is null or blank.</exception>
    /// <remarks>
    /// <para>
    /// The token is digested rather than published as the tag body, which buys two things. The
    /// grammar stays the one <see cref="ToContentHashEntityTag"/> serves, so a client revalidating a
    /// computed representation and one holding a row's version parse the same shape; and the storage
    /// token is not handed out verbatim, so a client cannot read a <c>rowversion</c> straight off the
    /// header and learn how often the row has been written to.
    /// </para>
    /// <para>
    /// That second part is obfuscation, not confidentiality, and the difference matters when the
    /// token is drawn from a small range. The digest is unkeyed, so anyone holding a tag can hash the
    /// candidates and find the counter behind it; against a low counter that search is trivial. A
    /// deployment whose write count is genuinely sensitive uses a high-entropy token — a
    /// <see cref="Guid"/>, or an opaque value of comparable width — rather than relying on the digest
    /// to hide a small one. Keying the digest instead would buy that back, at the price of a secret
    /// every replica has to share and rotate in step, which is not a trade this package makes for its
    /// callers.
    /// </para>
    /// <para>
    /// A single-segment canonical hash, so that a version and a one-element content digest of the
    /// same text agree. Deriving the tag needs nothing but the version value, which is what lets a
    /// list projection tag each of its items without a second read: one hash per row, over one short
    /// string.
    /// </para>
    /// <para>
    /// A token that is not already text — a <c>rowversion</c>, say — is converted by its caller,
    /// which is the only place that knows whether hexadecimal or base64 is the spelling the rest of
    /// the resource uses. Any spelling works as long as the resource keeps to one: the tag is opaque
    /// to the client, and only its stability matters.
    /// </para>
    /// </remarks>
    public static string ToVersionEntityTag(this string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("A version value is required to build an entity tag from one.", nameof(version));

        return new[] { version }.ToCanonicalHash().ToContentHashEntityTag();
    }

    /// <summary>
    /// Builds the tag standing for a resource whose concurrency token is a <see cref="Guid"/>.
    /// </summary>
    /// <param name="version">The resource's concurrency token.</param>
    /// <returns>A quoted strong entity tag of the form <c>"sha256-&lt;hex&gt;"</c>.</returns>
    /// <remarks>
    /// The format is stated — <c>N</c>, 32 hexadecimal digits with no braces and no hyphens — rather
    /// than left to the default, because two spellings of one version are two tags for one resource.
    /// Fixing it here is what keeps every caller from having to remember to fix it itself.
    /// </remarks>
    public static string ToVersionEntityTag(this Guid version) => version.ToString("N").ToVersionEntityTag();

    /// <summary>
    /// Keeps only the characters RFC 9110 allows inside a quoted entity tag.
    /// </summary>
    /// <param name="value">The candidate tag body.</param>
    /// <returns>The value with every other character removed.</returns>
    /// <remarks>
    /// The grammar is <c>etagc = %x21 / %x23-7E / obs-text</c>: printable ASCII apart from the
    /// double quote that delimits the tag, with the obsolete high range left out because nothing
    /// here produces it.
    /// </remarks>
    private static string Sanitize(string value)
    {
        var kept = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is >= '#' and <= '~' or '!')
                kept.Append(character);
        }

        return kept.ToString();
    }
}
