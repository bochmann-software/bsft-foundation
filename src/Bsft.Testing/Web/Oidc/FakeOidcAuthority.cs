using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bsft.Testing.Web.Oidc;

/// <summary>
/// An OpenID Connect authority that exists only inside the test process: it publishes a discovery
/// document and a key set, and exchanges authorization codes for signed identity tokens.
/// </summary>
/// <remarks>
/// <para>
/// It is what lets a federated login be tested end to end without a real identity provider in the
/// loop. The application under test is pointed at <see cref="CreateBackchannelHandler"/>, so discovery,
/// the key set and the token exchange all resolve in memory, over a real signature with a real key.
/// </para>
/// <para>
/// The authorization endpoint is deliberately not served. That leg happens in a browser, so a test
/// drives it itself: read the challenge redirect, call <see cref="IssueCode"/> with the nonce and
/// code challenge it carried, and request the callback. Faking the redirect too would skip exactly
/// the state and nonce handling worth testing.
/// </para>
/// </remarks>
public sealed class FakeOidcAuthority : IFakeOidcAuthority, IDisposable
{
    /// <summary>The issuer used when none is given. HTTPS, so a test need not relax metadata security.</summary>
    public const string DefaultIssuer = "https://fake-idp.local";

    /// <summary>How long an issued identity token stays valid.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    /// <summary>How long an issued logout token stays valid, matching the shipped authority's default.</summary>
    public static readonly TimeSpan LogoutTokenLifetime = TimeSpan.FromMinutes(2);

    /// <summary>The form field a logout token is delivered under, fixed by Back-Channel Logout 1.0 §2.5.</summary>
    public const string LogoutTokenField = "logout_token";

    /// <summary>The event key a logout token's <c>events</c> claim carries, fixed by the specification.</summary>
    public const string BackChannelLogoutEvent = "http://schemas.openid.net/event/backchannel-logout";

    /// <summary>The token type header a logout token carries.</summary>
    internal const string LogoutTokenType = "logout+jwt";

    /// <summary>The issuer stamped on a token whose defect is naming an authority nobody trusts.</summary>
    internal const string ForeignIssuer = "https://not-this-authority.local";

    /// <summary>The audience stamped on a token whose defect is being addressed elsewhere.</summary>
    internal const string ForeignAudience = "some-other-client";

    /// <summary>The event key stamped on a token whose defect is naming the wrong event.</summary>
    internal const string ForeignEvent = "http://schemas.openid.net/event/not-a-logout";

    /// <summary>How far in the past a deliberately stale token is stamped. Well past any sane replay window.</summary>
    internal static readonly TimeSpan StaleLogoutTokenAge = TimeSpan.FromHours(1);

    /// <summary>The modulus size of the signing key, matching what a real authority publishes.</summary>
    internal const int SigningKeySizeBits = 2048;

    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;
    private readonly ConcurrentDictionary<string, FakeOidcGrant> _grants = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<FakeOidcTokenRequest> _tokenRequests = new();
    private readonly ConcurrentDictionary<string, byte> _refreshTokens = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeOidcAuthority"/> class.
    /// </summary>
    /// <param name="issuer">The issuer to publish, or <see langword="null"/> for <see cref="DefaultIssuer"/>.</param>
    public FakeOidcAuthority(string? issuer = null)
    {
        Issuer = (issuer ?? DefaultIssuer).TrimEnd('/');
        _rsa = RSA.Create(SigningKeySizeBits);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "fake-oidc-key" };
    }

    /// <inheritdoc />
    public string Issuer { get; }

    /// <summary>
    /// Gets or sets the clock issued tokens are stamped from. Defaults to the system clock.
    /// </summary>
    /// <remarks>
    /// Settable rather than injected, so that the common case stays a parameterless construction and a
    /// test that needs an expired or not-yet-valid token can still say so.
    /// </remarks>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>
    /// Gets or sets the ways this authority's token endpoint departs from the specification.
    /// </summary>
    /// <remarks>
    /// Settable for the same reason <see cref="Clock"/> is: the common case stays a parameterless
    /// construction, and a test that needs a provider with a particular quirk says so on the line
    /// where it needs it. The default policy is the behaviour this authority had before the switches
    /// existed.
    /// </remarks>
    public FakeOidcTokenPolicy Policy { get; set; } = new();

    /// <summary>
    /// Creates a message handler that serves this authority's endpoints. Assign it to the
    /// application's OpenID Connect back channel.
    /// </summary>
    /// <returns>
    /// A handler bound to this authority. The caller owns it, which is why it is created on request
    /// rather than held here: an <see cref="HttpClient"/> disposes the handler it was given.
    /// </returns>
    public HttpMessageHandler CreateBackchannelHandler()
        => new FakeOidcBackchannelHandler(this, new FakeOidcTokenEndpoint(this));

    /// <inheritdoc />
    public IReadOnlyList<FakeOidcTokenRequest> TokenRequests => _tokenRequests.ToArray();

    /// <summary>
    /// Authorizes a person and returns the code that redeems into their identity token.
    /// </summary>
    /// <remarks>
    /// Pass the nonce and the code challenge the application's own authorization redirect carried, and
    /// the exchange is then checked the way a real authority checks it.
    /// </remarks>
    /// <param name="request">Who the authority will say this is, and what the authorization request carried.</param>
    /// <returns>The authorization code to present at the callback.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public string IssueCode(FakeOidcAuthorizationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var code = Guid.NewGuid().ToString("N");
        _grants[code] = new FakeOidcGrant(request.User, request.Nonce, request.CodeChallenge);

        return code;
    }

    /// <summary>
    /// Mints a Back-Channel Logout 1.0 logout token for one relying party.
    /// </summary>
    /// <param name="audience">The relying party the token is addressed to.</param>
    /// <param name="subject">The subject to name, or <see langword="null"/> to name none.</param>
    /// <param name="sessionId">The session to name, or <see langword="null"/> to name none.</param>
    /// <returns>The compact serialized logout token.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="audience"/> is blank.</exception>
    /// <remarks>
    /// Signed with the same key, the same <c>kid</c> and the same algorithm as the identity tokens, so
    /// a relying party that already trusts this authority needs no second trust anchor — which is the
    /// property the real specification relies on and therefore the one a test has to reproduce.
    /// </remarks>
    public string CreateLogoutToken(string audience, string? subject = null, string? sessionId = null)
        => CreateLogoutToken(new FakeLogoutTokenRequest(audience, subject, sessionId));

    /// <summary>
    /// Mints a logout token, optionally with one deliberate defect for a receiver's negative tests.
    /// </summary>
    /// <param name="request">What to assert, and what should be wrong with it.</param>
    /// <returns>The compact serialized logout token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the request names a blank audience.</exception>
    public string CreateLogoutToken(FakeLogoutTokenRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.Audience))
            throw new ArgumentException("A logout token must name the relying party it is for.", nameof(request));

        return FakeLogoutTokenFactory.Create(request, _signingKey, Issuer, Clock.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// Delivers a logout token to a relying party's back-channel endpoint the way this authority's
    /// dispatcher would.
    /// </summary>
    /// <param name="relyingParty">A client bound to the relying party under test. It must carry no cookies.</param>
    /// <param name="endpointPath">The receiver's path, relative to the client's base address.</param>
    /// <param name="request">What the token should assert, and what should be wrong with it.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>What the relying party answered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    /// Form-encoded under the single field name the specification fixes, because a receiver that only
    /// accepted JSON would pass a hand-rolled test and fail every real authority.
    /// </remarks>
    public Task<HttpResponseMessage> SendBackChannelLogoutAsync(
        HttpClient relyingParty,
        string endpointPath,
        FakeLogoutTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        if (relyingParty is null)
            throw new ArgumentNullException(nameof(relyingParty));

        if (endpointPath is null)
            throw new ArgumentNullException(nameof(endpointPath));

        return SendLogoutTokenAsync(relyingParty, endpointPath, CreateLogoutToken(request), cancellationToken);
    }

    /// <summary>
    /// Delivers an already-minted logout token, so a replay test can send the very same string twice.
    /// </summary>
    /// <param name="relyingParty">A client bound to the relying party under test.</param>
    /// <param name="endpointPath">The receiver's path, relative to the client's base address.</param>
    /// <param name="logoutToken">The token to post.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>What the relying party answered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static Task<HttpResponseMessage> SendLogoutTokenAsync(
        HttpClient relyingParty,
        string endpointPath,
        string logoutToken,
        CancellationToken cancellationToken = default)
    {
        if (relyingParty is null)
            throw new ArgumentNullException(nameof(relyingParty));

        if (endpointPath is null)
            throw new ArgumentNullException(nameof(endpointPath));

        if (logoutToken is null)
            throw new ArgumentNullException(nameof(logoutToken));

        var body = new FormUrlEncodedContent(
            new[] { new KeyValuePair<string, string>(LogoutTokenField, logoutToken) });

        return relyingParty.PostAsync(endpointPath, body, cancellationToken);
    }

    /// <summary>
    /// Invalidates every refresh token this authority has issued, as a provider does when a user
    /// withdraws consent.
    /// </summary>
    /// <remarks>
    /// The whole point of the connection lifecycle: after this, the next renewal is refused with
    /// <c>invalid_grant</c> and the connection has to break rather than keep retrying.
    /// </remarks>
    public void RevokeIssuedRefreshTokens() => _refreshTokens.Clear();

    /// <summary>Gets the refresh tokens this authority currently considers valid.</summary>
    public IReadOnlyCollection<string> ValidRefreshTokens => _refreshTokens.Keys.ToArray();

    /// <inheritdoc />
    public void Dispose() => _rsa.Dispose();

    /// <summary>Records a token request so a test can assert on what left the application.</summary>
    /// <param name="request">What the client sent.</param>
    /// <remarks>
    /// Recorded before the request is judged, and for every grant, so that a refusal is as observable
    /// as a success. A recorder that only saw the requests that worked would be useless for exactly
    /// the quirk tests it exists for.
    /// </remarks>
    internal void Record(FakeOidcTokenRequest request) => _tokenRequests.Enqueue(request);

    /// <summary>Mints an access token.</summary>
    /// <returns>The token.</returns>
    internal static string IssueAccessToken() => Guid.NewGuid().ToString("N");

    /// <summary>Mints a refresh token and remembers it as valid.</summary>
    /// <returns>The token.</returns>
    internal string IssueRefreshToken()
    {
        var token = Guid.NewGuid().ToString("N");
        _refreshTokens[token] = 0;

        return token;
    }

    /// <summary>
    /// Checks a presented refresh token, spending it when the policy says this authority rotates them.
    /// </summary>
    /// <param name="token">The token presented, which may be absent.</param>
    /// <returns><see langword="true"/> when the token was valid.</returns>
    internal bool TryConsumeRefreshToken(string? token)
    {
        if (token is null)
            return false;

        return Policy.AreRefreshTokensRotated ? _refreshTokens.TryRemove(token, out _) : _refreshTokens.ContainsKey(token);
    }

    /// <summary>Gets the key this authority signs with.</summary>
    internal RsaSecurityKey SigningKey => _signingKey;

    /// <summary>
    /// Redeems an authorization code, checking PKCE when the grant carries a challenge.
    /// </summary>
    /// <param name="request">What the client presented.</param>
    /// <returns>The grant, or <see langword="null"/> when the code is unknown or the verifier is wrong.</returns>
    internal FakeOidcGrant? Redeem(FakeOidcTokenRequest request)
    {
        if (request.Code is null || !_grants.TryRemove(request.Code, out var grant))
            return null;

        if (grant.CodeChallenge is null)
            return grant;

        return IsVerifierValid(request.CodeVerifier, grant.CodeChallenge) ? grant : null;
    }

    /// <summary>
    /// Builds a signed identity token for a redeemed grant.
    /// </summary>
    /// <param name="grant">The redeemed grant.</param>
    /// <param name="audience">The client the token is for.</param>
    /// <returns>The compact serialized identity token.</returns>
    internal string CreateIdToken(FakeOidcGrant grant, string audience)
    {
        var now = Clock.GetUtcNow().UtcDateTime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(TokenLifetime),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
            Claims = BuildClaims(grant)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>
    /// Assembles the payload a real authority would publish for this person.
    /// </summary>
    /// <param name="grant">The redeemed grant.</param>
    /// <returns>The claims, with arrays where the specification says arrays.</returns>
    private static Dictionary<string, object> BuildClaims(FakeOidcGrant grant)
    {
        var user = grant.User;

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sub"] = user.Subject
        };

        AddIfPresent(claims, "nonce", grant.Nonce);
        AddIfPresent(claims, "email", user.Email);
        AddIfPresent(claims, "name", user.Name);
        AddIfPresent(claims, "acr", user.AuthenticationContextClass);
        AddIfPresent(claims, "tenant_id", user.TenantId);
        AddIfPresent(claims, "sid", user.SessionId);

        if (user.Email is not null)
            claims["email_verified"] = user.IsEmailVerified;

        if (user.AuthenticationMethods is { Count: > 0 } methods)
            claims["amr"] = methods.ToArray();

        // Seconds since the Unix epoch, as the specification says, so a relying party's maximum-age
        // policy reads it exactly as it would read a real authority's.
        if (user.AuthenticatedAt is { } authenticatedAt)
            claims["auth_time"] = authenticatedAt.ToUnixTimeSeconds();

        if (user.OrganizationKeys is { Count: > 0 } groups)
            claims["groups"] = groups.ToArray();

        return claims;
    }

    /// <summary>
    /// Adds a claim when there is a value for it.
    /// </summary>
    /// <param name="claims">The payload being built.</param>
    /// <param name="name">The claim name.</param>
    /// <param name="value">The value, which may be absent.</param>
    private static void AddIfPresent(IDictionary<string, object> claims, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            claims[name] = value;
    }

    /// <summary>
    /// Checks a PKCE verifier against the challenge the authorization request carried.
    /// </summary>
    /// <param name="verifier">The verifier the client presented.</param>
    /// <param name="challenge">The challenge recorded with the grant.</param>
    /// <returns><see langword="true"/> when the verifier hashes to the challenge.</returns>
    private static bool IsVerifierValid(string? verifier, string challenge)
    {
        if (string.IsNullOrEmpty(verifier))
            return false;

        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));

        return string.Equals(Base64UrlEncoder.Encode(hash), challenge, StringComparison.Ordinal);
    }
}
