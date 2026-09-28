using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Bsft.Testing.Tests.Support;

/// <summary>
/// An application server: one P-256 key pair, and the ES256 tokens and headers it signs with it.
/// </summary>
/// <remarks>
/// Signing here rather than pasting a captured header is what lets a test say "this token was signed
/// by the holder of the key the header declares" and "this one was not" without the two differing in
/// any other way.
/// </remarks>
internal sealed class VapidTestSigner : IDisposable
{
    private const int CoordinateLength = 32;

    private static readonly string JwtHeader =
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("""{"typ":"JWT","alg":"ES256"}"""));

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    internal VapidTestSigner()
    {
        var point = new byte[1 + CoordinateLength + CoordinateLength];
        point[0] = 0x04;

        var parameters = _key.ExportParameters(includePrivateParameters: false);
        parameters.Q.X!.CopyTo(point.AsSpan(1));
        parameters.Q.Y!.CopyTo(point.AsSpan(1 + CoordinateLength));

        PublicKey = WebEncoders.Base64UrlEncode(point);
    }

    /// <summary>Gets the application server key, base64url of the uncompressed point.</summary>
    internal string PublicKey { get; }

    public void Dispose() => _key.Dispose();

    /// <summary>
    /// Renders the header a sender puts on a push.
    /// </summary>
    /// <param name="token">The JWT.</param>
    /// <param name="publicKey">The key to declare, which is not necessarily the signing one.</param>
    /// <returns>The <c>Authorization</c> header value.</returns>
    internal static string Header(string token, string publicKey) => $"vapid t={token}, k={publicKey}";

    /// <summary>
    /// Signs one claim set.
    /// </summary>
    /// <param name="claimsJson">The claims, as JSON.</param>
    /// <returns>The three-part JWT.</returns>
    internal string Sign(string claimsJson)
    {
        var signingInput = $"{JwtHeader}.{WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(claimsJson))}";

        // IEEE P1363, the raw r||s pair. The DER encoding .NET signs with by default is the classic
        // way to produce a token that looks right and verifies nowhere.
        var signature = _key.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{signingInput}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    /// <summary>
    /// Builds a three-part token whose payload is whatever is given, signature included but unusable.
    /// </summary>
    /// <param name="rawPayload">The middle part, verbatim.</param>
    /// <returns>The token.</returns>
    internal static string TokenWithRawPayload(string rawPayload) => $"{JwtHeader}.{rawPayload}.c2lnbmF0dXJl";

    /// <summary>
    /// Replaces a signed token's signature.
    /// </summary>
    /// <param name="token">The token to alter.</param>
    /// <param name="signature">The signature to put in its place.</param>
    /// <returns>The altered token.</returns>
    internal static string WithSignature(string token, string signature)
    {
        var parts = token.Split('.');

        return $"{parts[0]}.{parts[1]}.{signature}";
    }
}
