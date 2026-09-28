namespace Bsft.Testing.Web.Oidc;

/// <summary>
/// An OpenID Connect authority a test can drive: it publishes discovery metadata and a key set, and
/// exchanges authorization codes for signed identity tokens.
/// </summary>
/// <remarks>
/// Paired with its implementation the way the rest of this package pairs a harness with a contract,
/// so that a test fixture can hold the abstraction and a suite that needs a differently-behaving
/// authority — one that signs with the wrong key, or refuses every exchange — can substitute one
/// without the fixture noticing.
/// </remarks>
public interface IFakeOidcAuthority
{
    /// <summary>Gets the issuer this authority publishes and signs for.</summary>
    string Issuer { get; }

    /// <summary>Gets or sets the clock issued tokens are stamped from.</summary>
    TimeProvider Clock { get; set; }

    /// <summary>Gets or sets the ways this authority's token endpoint departs from the specification.</summary>
    FakeOidcTokenPolicy Policy { get; set; }

    /// <summary>Gets the refresh tokens this authority currently considers valid.</summary>
    IReadOnlyCollection<string> ValidRefreshTokens { get; }

    /// <summary>Invalidates every refresh token this authority has issued.</summary>
    void RevokeIssuedRefreshTokens();

    /// <summary>Gets the token requests this authority has received, oldest first.</summary>
    IReadOnlyList<FakeOidcTokenRequest> TokenRequests { get; }

    /// <summary>
    /// Creates a message handler that serves this authority's endpoints, for the application's
    /// OpenID Connect back channel.
    /// </summary>
    /// <returns>A handler bound to this authority, owned by the caller.</returns>
    HttpMessageHandler CreateBackchannelHandler();

    /// <summary>
    /// Authorizes a person and returns the code that redeems into their identity token.
    /// </summary>
    /// <param name="request">Who the authority will say this is, and what the authorization request carried.</param>
    /// <returns>The authorization code to present at the callback.</returns>
    string IssueCode(FakeOidcAuthorizationRequest request);
}
