using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Bsft.Testing.Web.Push;

/// <summary>
/// Reads a <c>vapid t=..., k=...</c> authorization header the way a push service reads it.
/// </summary>
/// <remarks>
/// Anything malformed yields <see langword="null"/> rather than an exception. A push service answers
/// 401 to a header it cannot read, and a fake that threw would turn "the sender got the header wrong"
/// — which is what a test wants to see — into a failure inside the transport.
/// </remarks>
internal static class VapidTokenReader
{
    private const string Scheme = "vapid ";
    private const string TokenParameter = "t=";
    private const string KeyParameter = "k=";
    private const int CoordinateLength = 32;
    private const int PointLength = 65;

    /// <summary>Header, payload and signature: a JWS compact serialization has exactly three parts.</summary>
    private const int JwtPartCount = 3;

    /// <summary>
    /// Reads and verifies one header value.
    /// </summary>
    /// <param name="header">The <c>Authorization</c> header value, or <c>null</c>.</param>
    /// <returns>The token, or <c>null</c> when the header is not a readable VAPID header.</returns>
    internal static FakeVapidToken? Read(string? header)
    {
        if (header is null || !header.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            return null;

        var parameters = header[Scheme.Length..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var token = Value(parameters, TokenParameter);
        var publicKey = Value(parameters, KeyParameter);

        if (token is null || publicKey is null)
            return null;

        var parts = token.Split('.');

        return parts.Length != JwtPartCount ? null : Build(parts, publicKey);
    }

    /// <summary>
    /// Reads one header parameter.
    /// </summary>
    /// <param name="parameters">The comma-separated parameters.</param>
    /// <param name="prefix">The parameter's prefix, including the equals sign.</param>
    /// <returns>The value, or <c>null</c> when the parameter is absent.</returns>
    private static string? Value(string[] parameters, string prefix)
    {
        var parameter = Array.Find(parameters, candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));

        return parameter?[prefix.Length..];
    }

    /// <summary>
    /// Reads the claims and verifies the signature.
    /// </summary>
    /// <param name="parts">The three parts of the JWT.</param>
    /// <param name="publicKey">The declared application server key, base64url.</param>
    /// <returns>The token, or <c>null</c> when the claims cannot be read.</returns>
    private static FakeVapidToken? Build(string[] parts, string publicKey)
    {
        JsonElement claims;

        try
        {
            claims = JsonSerializer.Deserialize<JsonElement>(WebEncoders.Base64UrlDecode(parts[1]));
        }
        catch (FormatException)
        {
            // Not base64url. A push service answers 401 to a header it cannot read, and a fake that
            // threw would turn "the sender got the header wrong" into a failure inside the transport.
            return null;
        }
        catch (JsonException)
        {
            // Base64url that does not decode to a JSON document. Same answer for the same reason.
            return null;
        }

        if (!claims.TryGetProperty("aud", out var audience)
            || !claims.TryGetProperty("sub", out var subject)
            || !claims.TryGetProperty("exp", out var expiry))
        {
            return null;
        }

        return new FakeVapidToken(
            audience.GetString() ?? string.Empty,
            subject.GetString() ?? string.Empty,
            DateTimeOffset.FromUnixTimeSeconds(expiry.GetInt64()),
            publicKey,
            IsSignatureValid(parts, publicKey));
    }

    /// <summary>
    /// Verifies the ES256 signature against the key the header declared.
    /// </summary>
    /// <param name="parts">The three parts of the JWT.</param>
    /// <param name="publicKey">The declared application server key, base64url.</param>
    /// <returns><see langword="true"/> when the signature verifies.</returns>
    private static bool IsSignatureValid(string[] parts, string publicKey)
    {
        byte[] point;
        byte[] signature;

        try
        {
            point = WebEncoders.Base64UrlDecode(publicKey);
            signature = WebEncoders.Base64UrlDecode(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (point.Length != PointLength)
            return false;

        try
        {
            using var key = ECDsa.Create(
                new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint
                    {
                        X = point[1..(1 + CoordinateLength)],
                        Y = point[(1 + CoordinateLength)..],
                    },
                });

            return key.VerifyData(
                Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception exception) when (exception is CryptographicException or PlatformNotSupportedException)
        {
            // A point that is not on the curve, or a signature of the wrong length. Both are "this
            // header does not verify", which is what the caller asked.
            //
            // The filter names both spellings because Windows reports a rejected import as "this
            // curve is not supported here", wrapping the real CryptographicException; catching only
            // the cryptographic one would let a malformed header escape the transport as an
            // exception, which is exactly what this reader exists to prevent. One clause rather
            // than two, because only one of them is reachable on any given platform.
            return false;
        }
    }
}
