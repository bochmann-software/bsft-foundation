using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Bsft.Testing.Web.Oidc;

/// <summary>
/// Serves the fake authority's back-channel endpoints — discovery, the key set, the token exchange,
/// revocation and a cheap authorized endpoint — without a socket.
/// </summary>
internal sealed class FakeOidcBackchannelHandler : HttpMessageHandler
{
    /// <summary>The discovery path every OpenID Connect client fetches first.</summary>
    internal const string DiscoveryPath = "/.well-known/openid-configuration";

    /// <summary>The path the key set is published on.</summary>
    internal const string JwksPath = "/.well-known/jwks";

    /// <summary>The path codes are exchanged on.</summary>
    internal const string TokenPath = "/protocol/token";

    /// <summary>The path tokens are revoked on.</summary>
    internal const string RevocationPath = "/protocol/revoke";

    /// <summary>
    /// A cheap authorized endpoint, for testing a connection health check against a provider that
    /// declares one.
    /// </summary>
    internal const string PingPath = "/api/ping";

    /// <summary>The path a client sends the user to in order to sign out.</summary>
    internal const string EndSessionPath = "/protocol/logout";

    /// <summary>The path a client sends the user to in order to sign in.</summary>
    internal const string AuthorizePath = "/protocol/authorize";

    private readonly FakeOidcAuthority _authority;
    private readonly FakeOidcTokenEndpoint _tokens;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeOidcBackchannelHandler"/> class.
    /// </summary>
    /// <param name="authority">The authority whose endpoints are served.</param>
    /// <param name="tokens">The token endpoint bound to that same authority.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    internal FakeOidcBackchannelHandler(FakeOidcAuthority authority, FakeOidcTokenEndpoint tokens)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.EndsWith(DiscoveryPath, StringComparison.Ordinal))
            return Json(BuildDiscoveryDocument());

        if (path.EndsWith(JwksPath, StringComparison.Ordinal))
            return Json(BuildKeySet());

        if (path.EndsWith(TokenPath, StringComparison.Ordinal))
            return await _tokens.ServeAsync(request, cancellationToken).ConfigureAwait(false);

        if (path.EndsWith(RevocationPath, StringComparison.Ordinal))
            return Revoke();

        if (path.EndsWith(PingPath, StringComparison.Ordinal))
            return Ping(request);

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Answers a revocation, or refuses it when the policy says this provider does not cooperate.
    /// </summary>
    /// <returns>The response.</returns>
    /// <remarks>
    /// RFC 7009 asks for 200 whether or not the token was known, so a successful answer here says
    /// nothing about whether anything was actually revoked — which is exactly the ambiguity the
    /// disconnect path has to live with against a real provider.
    /// </remarks>
    private HttpResponseMessage Revoke()
        => _authority.Policy.IsRevocationRefused
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK);

    /// <summary>
    /// Answers a cheap authorized call, refusing one that carries no bearer credential.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The response.</returns>
    /// <remarks>
    /// The 401 for a missing credential is what makes this usable for the retry test as well as the
    /// health one: a client whose token attachment is broken sees the same thing here that it would
    /// see at a real API.
    /// </remarks>
    private static HttpResponseMessage Ping(HttpRequestMessage request)
        => request.Headers.Authorization is { Scheme: "Bearer", Parameter.Length: > 0 }
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : new HttpResponseMessage(HttpStatusCode.Unauthorized);

    /// <summary>
    /// Builds the discovery document, naming only what this authority actually supports.
    /// </summary>
    /// <returns>The document.</returns>
    private Dictionary<string, object> BuildDiscoveryDocument()
    {
        var issuer = _authority.Issuer;

        return new Dictionary<string, object>
        {
            ["issuer"] = issuer,
            ["authorization_endpoint"] = issuer + AuthorizePath,
            ["token_endpoint"] = issuer + TokenPath,
            ["revocation_endpoint"] = issuer + RevocationPath,
            ["jwks_uri"] = issuer + JwksPath,
            ["end_session_endpoint"] = issuer + EndSessionPath,
            ["response_types_supported"] = new[] { "code" },
            ["response_modes_supported"] = new[] { "query", "form_post" },
            ["grant_types_supported"] = new[] { "authorization_code", "refresh_token", "client_credentials" },
            ["subject_types_supported"] = new[] { "public" },
            ["id_token_signing_alg_values_supported"] = new[] { SecurityAlgorithms.RsaSha256 },
            ["scopes_supported"] = new[] { "openid", "profile", "email" },
            ["code_challenge_methods_supported"] = new[] { "S256" },

            // Advertised so a relying party's own discovery-driven configuration sees an authority
            // that really does send logout tokens, and a test of the receiver is testing the same
            // shape a real deployment would negotiate.
            ["backchannel_logout_supported"] = true,
            ["backchannel_logout_session_supported"] = true
        };
    }

    /// <summary>
    /// Publishes the signing key as a JSON Web Key Set.
    /// </summary>
    /// <returns>The key set.</returns>
    private Dictionary<string, object> BuildKeySet()
    {
        var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(_authority.SigningKey);
        key.Use = "sig";
        key.Alg = SecurityAlgorithms.RsaSha256;

        return new Dictionary<string, object> { ["keys"] = new[] { key } };
    }

    /// <summary>
    /// Wraps a payload in a JSON response.
    /// </summary>
    /// <param name="payload">What to serialize.</param>
    /// <param name="status">The status code, defaulting to 200.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage Json(object payload, HttpStatusCode status = HttpStatusCode.OK)
        => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
}
