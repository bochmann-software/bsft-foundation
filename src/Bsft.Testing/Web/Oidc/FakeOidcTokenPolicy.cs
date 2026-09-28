using System.Net;

namespace Bsft.Testing.Web.Oidc;

/// <summary>
/// The ways <see cref="FakeOidcAuthority"/>'s token endpoint can be made to behave like a real-world
/// provider rather than like the specification.
/// </summary>
/// <remarks>
/// <para>
/// Every switch here corresponds to a quirk flag an outbound connector profile can carry, and exists
/// so that the flag can be tested against an authority that actually behaves that way. A client that
/// is only ever tested against a conformant authority is a client whose quirk handling is untested,
/// which is the same as not having it.
/// </para>
/// <para>
/// The defaults reproduce exactly what this authority did before the switches existed: an
/// authorization-code exchange, an identity token, a five-minute access token, no refresh token. A
/// test that sets nothing sees no change.
/// </para>
/// </remarks>
public sealed record FakeOidcTokenPolicy
{
    /// <summary>Gets a value indicating whether a refresh token is issued alongside the access token.</summary>
    public bool IsRefreshTokenIssued { get; init; }

    /// <summary>Gets a value indicating whether presenting a refresh token invalidates it and mints a new one.</summary>
    /// <remarks>
    /// The behaviour that punishes clients which renew more often than they need to, and the reason
    /// renewal has to be single-flight: two replicas renewing at once means the second presents a token
    /// the first has already spent.
    /// </remarks>
    public bool AreRefreshTokensRotated { get; init; }

    /// <summary>Gets a value indicating whether every refresh grant is refused as <c>invalid_grant</c>.</summary>
    public bool IsRefreshGrantRejected { get; init; }

    /// <summary>Gets a value indicating whether every grant of any kind is refused as <c>invalid_grant</c>.</summary>
    /// <remarks>
    /// For the terminal path on the flows that have no refresh token to revoke: a machine grant whose
    /// credentials the provider has disabled looks like this and nothing else.
    /// </remarks>
    public bool IsEveryGrantRejected { get; init; }

    /// <summary>Gets a value indicating whether client credentials in the request body are refused.</summary>
    public bool IsBasicAuthenticationRequired { get; init; }

    /// <summary>Gets a value indicating whether client credentials in an Authorization header are refused.</summary>
    public bool AreCredentialsRequiredInRequestBody { get; init; }

    /// <summary>Gets a value indicating whether a request carrying a proof-key verifier is refused.</summary>
    /// <remarks>The pre-RFC-7636 deployments the no-proof-key quirk flag exists for.</remarks>
    public bool IsProofKeyRejected { get; init; }

    /// <summary>Gets a value indicating whether the token response omits <c>expires_in</c> entirely.</summary>
    public bool IsExpiresInOmitted { get; init; }

    /// <summary>Gets a value indicating whether <c>expires_in</c> is written as a string rather than a number.</summary>
    public bool IsExpiresInWrittenAsAString { get; init; }

    /// <summary>Gets how long issued access tokens live.</summary>
    public TimeSpan AccessTokenLifetime { get; init; } = FakeOidcAuthority.TokenLifetime;

    /// <summary>Gets the scope the response reports as granted, or <see langword="null"/> to report none.</summary>
    public string? GrantedScope { get; init; }

    /// <summary>Gets a status the token endpoint answers with regardless, or <see langword="null"/> to behave.</summary>
    /// <remarks>For provoking the transient-failure path: a 500, a 503, or a 429.</remarks>
    public HttpStatusCode? ForcedTokenStatus { get; init; }

    /// <summary>Gets a value indicating whether the revocation endpoint refuses.</summary>
    public bool IsRevocationRefused { get; init; }
}
