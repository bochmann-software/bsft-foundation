namespace Bsft.Testing.Web.Oidc;

/// <summary>
/// What a client sent to <see cref="Bsft.Testing.Web.Oidc.FakeOidcAuthority"/>'s token endpoint, recorded so a test can
/// assert on it.
/// </summary>
/// <remarks>
/// It exists mainly for one assertion: that <see cref="CodeVerifier"/> is there. PKCE being switched
/// on is a property of the request that leaves the application, and the only way to prove it is to
/// look at what the authority received. The properties below the constructor are the same idea for
/// the outbound-connector quirks: where the client credentials went and whether the scope was
/// repeated are properties of the request, and nothing else can observe them.
/// </remarks>
/// <param name="GrantType">The grant type the client asked for.</param>
/// <param name="Code">The authorization code presented.</param>
/// <param name="CodeVerifier">The PKCE verifier presented, or <see langword="null"/> when none was.</param>
/// <param name="ClientId">The client identifier presented in the body, if it was.</param>
/// <param name="RedirectUri">The redirect URI presented.</param>
public sealed record FakeOidcTokenRequest(
    string? GrantType,
    string? Code,
    string? CodeVerifier,
    string? ClientId,
    string? RedirectUri)
{
    /// <summary>Gets the refresh token presented, on a renewal.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Gets the scope the request repeated, when it repeated one.</summary>
    public string? Scope { get; init; }

    /// <summary>Gets the client secret presented in the body, when it was sent there.</summary>
    /// <remarks>
    /// Recorded so a test can assert <em>where</em> the credentials went, which is the whole content of
    /// the request-body-credentials quirk. It is a fake authority in a test process; there is no secret
    /// here worth protecting.
    /// </remarks>
    public string? ClientSecret { get; init; }

    /// <summary>Gets a value indicating whether the request carried an HTTP Basic authorization header.</summary>
    public bool HasBasicAuthentication { get; init; }
}
