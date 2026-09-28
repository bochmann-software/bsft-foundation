using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace Bsft.Testing.Web.Oidc;

/// <summary>
/// Serves <see cref="FakeOidcAuthority"/>'s token endpoint across the three grants, applying whatever
/// quirks the authority's policy declares.
/// </summary>
/// <remarks>
/// Split out of the back-channel handler because the handler routes and this decides. The three
/// grants plus the credential-placement and proof-key checks are most of what a real token endpoint
/// does, and leaving them inside a router would make the router the largest thing in this package.
/// </remarks>
internal sealed class FakeOidcTokenEndpoint
{
    private readonly FakeOidcAuthority _authority;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeOidcTokenEndpoint"/> class.
    /// </summary>
    /// <param name="authority">The authority whose grants are served.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authority"/> is null.</exception>
    public FakeOidcTokenEndpoint(FakeOidcAuthority authority)
        => _authority = authority ?? throw new ArgumentNullException(nameof(authority));

    /// <summary>
    /// Serves a token request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The response.</returns>
    public async Task<HttpResponseMessage> ServeAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var form = await ReadFormAsync(request, cancellationToken).ConfigureAwait(false);
        var hasBasic = request.Headers.Authorization is { Scheme: "Basic" };

        var recorded = new FakeOidcTokenRequest(
            Read(form, "grant_type"),
            Read(form, "code"),
            Read(form, "code_verifier"),
            Read(form, "client_id"),
            Read(form, "redirect_uri"))
        {
            RefreshToken = Read(form, "refresh_token"),
            Scope = Read(form, "scope"),
            ClientSecret = Read(form, "client_secret"),
            HasBasicAuthentication = hasBasic,
        };

        _authority.Record(recorded);

        var policy = _authority.Policy;

        if (policy.ForcedTokenStatus is { } forced)
            return Error("server_error", forced);

        if (policy.IsEveryGrantRejected)
            return Error("invalid_grant", HttpStatusCode.BadRequest);

        return CheckCredentials(policy, recorded) ?? Grant(policy, recorded);
    }

    /// <summary>
    /// Enforces the two credential-placement quirks and the proof-key one.
    /// </summary>
    /// <param name="policy">The authority's policy.</param>
    /// <param name="request">What the client sent.</param>
    /// <returns>The refusal, or <see langword="null"/> when the request is acceptable.</returns>
    private static HttpResponseMessage? CheckCredentials(FakeOidcTokenPolicy policy, FakeOidcTokenRequest request)
    {
        if (policy.IsBasicAuthenticationRequired && !request.HasBasicAuthentication)
            return Error("invalid_client", HttpStatusCode.Unauthorized);

        if (policy.AreCredentialsRequiredInRequestBody && request.HasBasicAuthentication)
            return Error("invalid_client", HttpStatusCode.Unauthorized);

        return policy.IsProofKeyRejected && request.CodeVerifier is not null
            ? Error("invalid_request", HttpStatusCode.BadRequest)
            : null;
    }

    /// <summary>
    /// Dispatches on the grant type.
    /// </summary>
    /// <param name="policy">The authority's policy.</param>
    /// <param name="request">What the client sent.</param>
    /// <returns>The response.</returns>
    private HttpResponseMessage Grant(FakeOidcTokenPolicy policy, FakeOidcTokenRequest request)
        => request.GrantType switch
        {
            "refresh_token" => Renew(policy, request),
            "client_credentials" => Json(BuildTokenResponse(policy, identityToken: null)),
            _ => Exchange(policy, request),
        };

    /// <summary>Redeems an authorization code.</summary>
    /// <param name="policy">The authority's policy.</param>
    /// <param name="request">What the client sent.</param>
    /// <returns>The response.</returns>
    private HttpResponseMessage Exchange(FakeOidcTokenPolicy policy, FakeOidcTokenRequest request)
    {
        var grant = _authority.Redeem(request);

        if (grant is null)
            return Error("invalid_grant", HttpStatusCode.BadRequest);

        var identityToken = _authority.CreateIdToken(grant, request.ClientId ?? string.Empty);

        return Json(BuildTokenResponse(policy, identityToken));
    }

    /// <summary>Renews with a refresh token.</summary>
    /// <param name="policy">The authority's policy.</param>
    /// <param name="request">What the client sent.</param>
    /// <returns>The response.</returns>
    private HttpResponseMessage Renew(FakeOidcTokenPolicy policy, FakeOidcTokenRequest request)
    {
        if (policy.IsRefreshGrantRejected || !_authority.TryConsumeRefreshToken(request.RefreshToken))
            return Error("invalid_grant", HttpStatusCode.BadRequest);

        return Json(BuildTokenResponse(policy, identityToken: null));
    }

    /// <summary>
    /// Builds a successful token response in whichever shape the policy asks for.
    /// </summary>
    /// <param name="policy">The authority's policy.</param>
    /// <param name="identityToken">The identity token, on the grants that carry one.</param>
    /// <returns>The payload.</returns>
    private Dictionary<string, object> BuildTokenResponse(FakeOidcTokenPolicy policy, string? identityToken)
    {
        var payload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["access_token"] = FakeOidcAuthority.IssueAccessToken(),
            ["token_type"] = "Bearer",
        };

        if (identityToken is not null)
            payload["id_token"] = identityToken;

        if (!policy.IsExpiresInOmitted)
        {
            var seconds = (int)policy.AccessTokenLifetime.TotalSeconds;
            payload["expires_in"] = policy.IsExpiresInWrittenAsAString
                ? seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : seconds;
        }

        if (policy.IsRefreshTokenIssued)
            payload["refresh_token"] = _authority.IssueRefreshToken();

        if (policy.GrantedScope is { } scope)
            payload["scope"] = scope;

        return payload;
    }

    /// <summary>Reads the request body as a form.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The parsed form.</returns>
    private static async Task<Dictionary<string, StringValues>> ReadFormAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => request.Content is null
            ? []
            : QueryHelpers.ParseQuery(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

    private static string? Read(IReadOnlyDictionary<string, StringValues> form, string name)
        => form.TryGetValue(name, out var value) ? value.ToString() : null;

    private static HttpResponseMessage Error(string code, HttpStatusCode status)
        => Json(new Dictionary<string, object> { ["error"] = code }, status);

    private static HttpResponseMessage Json(object payload, HttpStatusCode status = HttpStatusCode.OK)
        => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
}
