namespace Bsft.Testing.Web.Oidc;

/// <summary>
/// What the application's own authorization redirect carried, handed back to the fake authority so
/// it can check the exchange the way a real one does.
/// </summary>
/// <param name="User">Who the authority will say this is.</param>
/// <param name="Nonce">The nonce from the authorization request, echoed into the identity token.</param>
/// <param name="CodeChallenge">
/// The PKCE challenge from the authorization request. When given, the token endpoint verifies the
/// verifier against it and refuses the exchange if it does not match.
/// </param>
public sealed record FakeOidcAuthorizationRequest(FakeOidcUser User, string? Nonce = null, string? CodeChallenge = null);
