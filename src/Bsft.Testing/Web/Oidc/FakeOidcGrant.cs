namespace Bsft.Testing.Web.Oidc;

/// <summary>
/// An authorization the fake authority has granted and not yet redeemed.
/// </summary>
/// <param name="User">Who was authorized.</param>
/// <param name="Nonce">The nonce to echo into the identity token.</param>
/// <param name="CodeChallenge">The PKCE challenge the redemption must match, if one was recorded.</param>
internal sealed record FakeOidcGrant(FakeOidcUser User, string? Nonce, string? CodeChallenge);
