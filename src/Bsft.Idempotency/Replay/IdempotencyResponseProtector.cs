using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Bsft.Idempotency.Replay;

/// <summary>
/// The shipped <see cref="IIdempotencyResponseProtector"/>, over ASP.NET Core Data Protection.
/// </summary>
/// <remarks>
/// <para>
/// Data Protection rather than the framework's secret protector, because this is a Foundation package
/// and may reference Foundation packages only; Data Protection ships with the ASP.NET Core shared
/// framework the package already references, so sealing costs no new dependency. A host whose replicas
/// share one key ring (which <c>Bsft.Security</c> requires of a multi-instance host) replays on any of them; a host whose
/// replicas each hold their own answers a replay on another replica with the withheld 409, which is the
/// safe failure.
/// </para>
/// <para>
/// A sealed body starts with a fixed marker, so a sealed record is recognised whatever the endpoint's
/// metadata says at replay time: removing the marker from a route within the replay window does not
/// replay ciphertext as if it were the response.
/// </para>
/// <para>
/// Each body is sealed under a purpose chain that ends in the record's caller scope and key, so the
/// ciphertext only opens for the record it was written into. A body copied into another record, by
/// anyone who can write to the store but holds no key, is withheld there instead of replayed to that
/// record's caller.
/// </para>
/// </remarks>
internal sealed class IdempotencyResponseProtector : IIdempotencyResponseProtector
{
    /// <summary>The Data Protection purpose the bodies are sealed under.</summary>
    internal const string Purpose = "Bsft.Idempotency.ResponseBody.v1";

    /// <summary>The prefix of a sealed body: a NUL-delimited name no JSON or text response starts with.</summary>
    private static readonly byte[] Marker = "\0bsft-idempotency-sealed-v1\0"u8.ToArray();

    private readonly IDataProtector _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyResponseProtector"/> class.
    /// </summary>
    /// <param name="provider">The host's Data Protection provider.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is null.</exception>
    public IdempotencyResponseProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _root = provider.CreateProtector(Purpose);
    }

    /// <inheritdoc />
    public byte[]? Protect(byte[] body, string scope, string key)
    {
        ArgumentNullException.ThrowIfNull(body);

        var protector = ForRecord(scope, key);

        byte[] sealedBody;

        try
        {
            sealedBody = protector.Protect(body);
        }
        catch (CryptographicException)
        {
            // No usable key: storing the plaintext instead is exactly what the marker forbids, so nothing
            // is stored and the request is merely not replayable.
            return null;
        }

        var marked = new byte[Marker.Length + sealedBody.Length];

        Marker.CopyTo(marked, 0);
        sealedBody.CopyTo(marked, Marker.Length);

        return marked;
    }

    /// <inheritdoc />
    public byte[]? FindReplayableBody(byte[] storedBody, bool isSensitive, string scope, string key)
    {
        ArgumentNullException.ThrowIfNull(storedBody);

        var protector = ForRecord(scope, key);

        if (!storedBody.AsSpan().StartsWith(Marker))
            return isSensitive ? null : storedBody;

        try
        {
            return protector.Unprotect(storedBody[Marker.Length..]);
        }
        catch (CryptographicException)
        {
            // A key ring that is not shared, a revoked key, a damaged row or a body copied in from another
            // record: nothing readable remains, and the caller is told the secret was issued rather than
            // shown anything.
            return null;
        }
    }

    /// <summary>The protector of one record: the package's purpose, then the record's scope and key.</summary>
    /// <param name="scope">The caller digest the key was claimed under.</param>
    /// <param name="key">The idempotency key the client sent.</param>
    /// <returns>A protector whose ciphertext only this record's scope and key open.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope"/> or <paramref name="key"/> is null.</exception>
    private IDataProtector ForRecord(string scope, string key)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(key);

        return _root.CreateProtector(scope, key);
    }
}
