using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bsft.Testing.Web.Oidc;

/// <summary>
/// Mints the fake authority's logout tokens: the clean one a receiver must accept, and the ten
/// deliberately defective ones it must refuse.
/// </summary>
/// <remarks>
/// <para>
/// Split out of <see cref="FakeOidcAuthority"/> because it is a different job. The authority serves
/// discovery, a key set and a token exchange; this mints one signed assertion and knows, for each
/// defect, precisely which part of it to break. The two share only the signing key — which is the
/// point, because a receiver that already trusts the authority must need no second trust anchor.
/// </para>
/// <para>
/// Every defect is expressed as a departure from the same clean token rather than as its own builder,
/// so a reader can see what a valid logout token is and what each negative case changes about it.
/// </para>
/// </remarks>
internal static class FakeLogoutTokenFactory
{
    /// <summary>
    /// Mints one logout token.
    /// </summary>
    /// <param name="request">What to assert, and what should be wrong with it.</param>
    /// <param name="signingKey">The authority's own key, so a clean token validates against the published set.</param>
    /// <param name="issuer">The authority's issuer.</param>
    /// <param name="now">The instant the authority is minting at.</param>
    /// <returns>The compact serialized token.</returns>
    internal static string Create(FakeLogoutTokenRequest request, RsaSecurityKey signingKey, string issuer, DateTime now)
    {
        var issuedAt = request.Defect == FakeLogoutTokenDefect.StaleIssuedAt
            ? now - FakeOidcAuthority.StaleLogoutTokenAge
            : now;

        var claims = BuildClaims(request);

        // Held here rather than inside Credentials so the handle lives exactly as long as the signing
        // does: it is a native key, and one is created for every token given this defect.
        using var foreign = ForeignKey(request.Defect);

        var credentials = Credentials(signingKey, foreign);
        var named = request.Defect == FakeLogoutTokenDefect.UnknownIssuer ? FakeOidcAuthority.ForeignIssuer : issuer;
        var audience = request.Defect == FakeLogoutTokenDefect.WrongAudience ? FakeOidcAuthority.ForeignAudience : request.Audience;

        // The descriptor always writes an iat, defaulting it to now when none is given, so the one
        // defect that needs the claim genuinely absent is assembled as a raw payload instead.
        if (request.Defect == FakeLogoutTokenDefect.IssuedAtMissing)
            return WithoutIssuedAt(claims, named, audience, issuedAt, credentials);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = named,
            Audience = audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = issuedAt.Add(FakeOidcAuthority.LogoutTokenLifetime),
            TokenType = FakeOidcAuthority.LogoutTokenType,
            SigningCredentials = credentials,
            Claims = claims
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>
    /// Signs a token whose payload deliberately carries no <c>iat</c>.
    /// </summary>
    /// <param name="claims">The payload built for the request.</param>
    /// <param name="issuer">The issuer to name.</param>
    /// <param name="audience">The audience to name.</param>
    /// <param name="notBefore">The instant the token becomes usable.</param>
    /// <param name="credentials">What to sign it with.</param>
    /// <returns>The compact serialized token.</returns>
    /// <remarks>
    /// Assembled as raw JSON because <see cref="SecurityTokenDescriptor"/> writes an <c>iat</c>
    /// unconditionally, defaulting it to the current instant, so the descriptor path cannot express
    /// "the authority omitted it" — which is the one thing this defect exists to express.
    /// </remarks>
    private static string WithoutIssuedAt(
        Dictionary<string, object> claims,
        string issuer,
        string audience,
        DateTime notBefore,
        SigningCredentials credentials)
    {
        var payload = new Dictionary<string, object>(claims, StringComparer.Ordinal)
        {
            ["iss"] = issuer,
            ["aud"] = audience,
            ["nbf"] = new DateTimeOffset(notBefore, TimeSpan.Zero).ToUnixTimeSeconds(),
            ["exp"] = new DateTimeOffset(notBefore.Add(FakeOidcAuthority.LogoutTokenLifetime), TimeSpan.Zero).ToUnixTimeSeconds(),
        };

        return new JsonWebTokenHandler().CreateToken(
            JsonSerializer.Serialize(payload),
            credentials,
            new Dictionary<string, object>(StringComparer.Ordinal) { ["typ"] = FakeOidcAuthority.LogoutTokenType });
    }

    /// <summary>
    /// Chooses the credentials a logout token is signed with.
    /// </summary>
    /// <param name="signingKey">The authority's own key.</param>
    /// <param name="foreign">The throwaway key, or <see langword="null"/> to sign with the authority's own.</param>
    /// <returns>This authority's own credentials, or a throwaway key it never published.</returns>
    /// <remarks>
    /// The foreign key deliberately carries the genuine key's identifier: a receiver that resolves the
    /// key by <c>kid</c> and stops there would accept it, which is exactly the mistake this defect
    /// exists to catch.
    /// </remarks>
    private static SigningCredentials Credentials(RsaSecurityKey signingKey, RSA? foreign)
        => foreign is null
            ? new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
            : new SigningCredentials(
                new RsaSecurityKey(foreign) { KeyId = signingKey.KeyId }, SecurityAlgorithms.RsaSha256);

    /// <summary>Creates the key a token nobody published is signed with, for the one defect that needs it.</summary>
    /// <param name="defect">The defect the token is being given, if any.</param>
    /// <returns>The throwaway key, or <see langword="null"/> when the authority signs with its own.</returns>
    private static RSA? ForeignKey(FakeLogoutTokenDefect defect)
        => defect == FakeLogoutTokenDefect.ForeignSigningKey
            ? RSA.Create(FakeOidcAuthority.SigningKeySizeBits)
            : null;

    /// <summary>
    /// Assembles a logout token's payload, applying whichever defect was asked for.
    /// </summary>
    /// <param name="request">What to assert, and what should be wrong with it.</param>
    /// <returns>The claims.</returns>
    /// <remarks>
    /// <c>nonce</c> is absent unless the defect asks for it, because §2.4 forbids it and a receiver
    /// that tolerated one would accept an identity token replayed as a logout token.
    /// </remarks>
    private static Dictionary<string, object> BuildClaims(FakeLogoutTokenRequest request)
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal);

        if (request.Defect != FakeLogoutTokenDefect.JwtIdMissing)
            claims["jti"] = request.TokenId ?? Guid.NewGuid().ToString("N");

        if (request.Defect != FakeLogoutTokenDefect.NoSubjectAndNoSessionId)
        {
            AddIfPresent(claims, "sub", request.Subject);
            AddIfPresent(claims, "sid", request.SessionId);
        }

        if (request.Defect == FakeLogoutTokenDefect.NoncePresent)
            claims["nonce"] = Guid.NewGuid().ToString("N");

        if (request.Defect == FakeLogoutTokenDefect.EventsMissing)
            return claims;

        var eventKey = request.Defect == FakeLogoutTokenDefect.EventsWrongKey
            ? FakeOidcAuthority.ForeignEvent
            : FakeOidcAuthority.BackChannelLogoutEvent;

        claims["events"] = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [eventKey] = new Dictionary<string, object>(StringComparer.Ordinal)
        };

        return claims;
    }

    /// <summary>
    /// Adds a claim when there is a value for it.
    /// </summary>
    /// <param name="claims">The payload being assembled.</param>
    /// <param name="name">The claim name.</param>
    /// <param name="value">The value, or <see langword="null"/> to add nothing.</param>
    private static void AddIfPresent(IDictionary<string, object> claims, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            claims[name] = value;
    }
}
