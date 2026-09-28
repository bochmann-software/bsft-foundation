using System.Net;
using System.Text;
using System.Text.Json;
using Bsft.Testing.Web;
using Bsft.Testing.Web.Oidc;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Bsft.Testing.Tests.Web.Oidc;

/// <summary>
/// The in-process OpenID Connect authority: what it publishes, what it signs, and the two checks it
/// performs on an exchange so that a test proving PKCE actually proves it.
/// </summary>
public sealed class FakeOidcAuthorityTests
{
    private static readonly FakeOidcUser Person = new(
        "sub-1",
        "person@example.com",
        IsEmailVerified: true,
        Name: "A Person",
        AuthenticationMethods: new[] { "pwd", "otp" },
        AuthenticationContextClass: "silver",
        TenantId: "tenant-a",
        OrganizationKeys: new[] { "engineering" });

    [Fact]
    public void Issuer_DefaultsToTheHttpsPlaceholderAndLosesATrailingSlash()
    {
        using var standard = new FakeOidcAuthority();
        using var custom = new FakeOidcAuthority("https://idp.test/");

        standard.Issuer.Should().Be(FakeOidcAuthority.DefaultIssuer);
        custom.Issuer.Should().Be("https://idp.test");
    }

    [Fact]
    public async Task Discovery_PublishesTheEndpointsAClientNeeds()
    {
        using var authority = new FakeOidcAuthority();

        var document = await GetJsonAsync(authority, "/.well-known/openid-configuration");

        document.GetProperty("issuer").GetString().Should().Be(authority.Issuer);
        document.GetProperty("token_endpoint").GetString().Should().StartWith(authority.Issuer);
        document.GetProperty("authorization_endpoint").GetString().Should().StartWith(authority.Issuer);
        document.GetProperty("end_session_endpoint").GetString().Should().StartWith(authority.Issuer);
        document.GetProperty("code_challenge_methods_supported")[0].GetString().Should().Be("S256");
    }

    [Fact]
    public async Task KeySet_PublishesTheSigningKey()
    {
        using var authority = new FakeOidcAuthority();

        var document = await GetJsonAsync(authority, "/.well-known/jwks");

        var key = document.GetProperty("keys")[0];
        key.GetProperty("use").GetString().Should().Be("sig");
        key.GetProperty("kty").GetString().Should().Be("RSA");
    }

    [Fact]
    public async Task UnknownPath_IsNotFound()
    {
        using var authority = new FakeOidcAuthority();
        using var client = Client(authority);

        var response = await client.GetAsync(authority.Issuer + "/nothing-here");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void IssueCode_WithoutARequest_Throws()
    {
        using var authority = new FakeOidcAuthority();

        var issue = () => authority.IssueCode(null!);

        issue.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task TokenEndpoint_ExchangesACodeForASignedIdentityToken()
    {
        using var authority = new FakeOidcAuthority();
        var code = authority.IssueCode(new FakeOidcAuthorizationRequest(Person, "nonce-1"));

        var token = await ExchangeAsync(authority, code);

        var payload = new JsonWebToken(token.GetProperty("id_token").GetString());
        payload.Issuer.Should().Be(authority.Issuer);
        payload.Audiences.Should().ContainSingle().Which.Should().Be("test-client");
        payload.GetClaim("sub").Value.Should().Be("sub-1");
        payload.GetClaim("nonce").Value.Should().Be("nonce-1");
        payload.GetClaim("email").Value.Should().Be("person@example.com");
        payload.GetClaim("email_verified").Value.Should().Be("true");
        payload.GetClaim("acr").Value.Should().Be("silver");
        payload.GetClaim("tenant_id").Value.Should().Be("tenant-a");
    }

    [Fact]
    public async Task TokenEndpoint_WritesAmrAndGroupsAsArrays()
    {
        using var authority = new FakeOidcAuthority();
        var code = authority.IssueCode(new FakeOidcAuthorizationRequest(Person));

        var token = await ExchangeAsync(authority, code);

        var payload = new JsonWebToken(token.GetProperty("id_token").GetString());
        payload.GetPayloadValue<string[]>("amr").Should().BeEquivalentTo("pwd", "otp");
        payload.GetPayloadValue<string[]>("groups").Should().BeEquivalentTo("engineering");
    }

    [Fact]
    public async Task TokenEndpoint_WhenTheUserCarriesAnAuthenticationTime_PublishesItAsEpochSeconds()
    {
        // What lets a test drive a maximum-age policy: a user stamped in the past fails a freshness
        // rule and the same user stamped now passes it, with no waiting and no clock to move.
        using var authority = new FakeOidcAuthority();
        var authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(1786000000);
        var code = authority.IssueCode(new FakeOidcAuthorizationRequest(
            Person with { AuthenticatedAt = authenticatedAt }));

        var token = await ExchangeAsync(authority, code);

        var payload = new JsonWebToken(token.GetProperty("id_token").GetString());
        payload.GetPayloadValue<long>("auth_time").Should().Be(1786000000);
    }

    [Fact]
    public async Task TokenEndpoint_OmitsWhatTheUserDidNotCarry()
    {
        using var authority = new FakeOidcAuthority();
        var code = authority.IssueCode(new FakeOidcAuthorizationRequest(new FakeOidcUser("sub-2")));

        var token = await ExchangeAsync(authority, code);

        var payload = new JsonWebToken(token.GetProperty("id_token").GetString());
        payload.TryGetClaim("email", out _).Should().BeFalse();
        payload.TryGetClaim("email_verified", out _).Should().BeFalse();
        payload.TryGetClaim("amr", out _).Should().BeFalse();
        payload.TryGetClaim("auth_time", out _).Should().BeFalse();
        payload.TryGetClaim("groups", out _).Should().BeFalse();
        payload.TryGetClaim("nonce", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TokenEndpoint_StampsTheTokenFromTheConfiguredClock()
    {
        var issuedAt = new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);
        using var authority = new FakeOidcAuthority { Clock = new StoppedClock(issuedAt) };
        var code = authority.IssueCode(new FakeOidcAuthorizationRequest(Person));

        var token = await ExchangeAsync(authority, code);

        var payload = new JsonWebToken(token.GetProperty("id_token").GetString());
        payload.ValidTo.Should().BeCloseTo(issuedAt.Add(FakeOidcAuthority.TokenLifetime).UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task TokenEndpoint_RecordsWhatTheClientPresented()
    {
        using var authority = new FakeOidcAuthority();
        var code = authority.IssueCode(new FakeOidcAuthorizationRequest(Person));

        await ExchangeAsync(authority, code);

        var request = authority.TokenRequests.Should().ContainSingle().Subject;
        request.GrantType.Should().Be("authorization_code");
        request.Code.Should().Be(code);
        request.ClientId.Should().Be("test-client");
        request.RedirectUri.Should().Be("https://localhost/signin-oidc");
    }

    [Fact]
    public async Task TokenEndpoint_RefusesAnUnknownCode()
    {
        using var authority = new FakeOidcAuthority();

        var response = await PostAsync(authority, "code=never-issued&client_id=test-client");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TokenEndpoint_RefusesTheSameCodeTwice()
    {
        using var authority = new FakeOidcAuthority();
        var code = authority.IssueCode(new FakeOidcAuthorizationRequest(Person));

        await ExchangeAsync(authority, code);
        var replay = await PostAsync(authority, $"code={code}&client_id=test-client");

        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TokenEndpoint_WithoutAContent_RefusesTheExchange()
    {
        using var authority = new FakeOidcAuthority();
        using var client = Client(authority);

        var response = await client.PostAsync(
            authority.Issuer + "/protocol/token",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TokenEndpoint_AcceptsTheVerifierThatHashesToTheChallenge()
    {
        const string Verifier = "a-code-verifier-of-sufficient-length-1234567890";
        using var authority = new FakeOidcAuthority();
        var code = authority.IssueCode(new FakeOidcAuthorizationRequest(Person, null, Challenge(Verifier)));

        var response = await PostAsync(authority, $"code={code}&client_id=test-client&code_verifier={Verifier}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("a-different-verifier-entirely-0987654321")]
    [InlineData("")]
    public async Task TokenEndpoint_RefusesAVerifierThatDoesNotMatchTheChallenge(string verifier)
    {
        using var authority = new FakeOidcAuthority();
        var code = authority.IssueCode(new FakeOidcAuthorizationRequest(
            Person,
            null,
            Challenge("a-code-verifier-of-sufficient-length-1234567890")));

        var response = await PostAsync(authority, $"code={code}&client_id=test-client&code_verifier={verifier}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static string Challenge(string verifier)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(verifier));

        return Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(hash);
    }

    private static HttpClient Client(IFakeOidcAuthority authority) => new(authority.CreateBackchannelHandler());

    private static async Task<JsonElement> GetJsonAsync(IFakeOidcAuthority authority, string path)
    {
        using var client = Client(authority);
        var body = await client.GetStringAsync(authority.Issuer + path);

        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<HttpResponseMessage> PostAsync(IFakeOidcAuthority authority, string form)
    {
        using var client = Client(authority);

        return await client.PostAsync(
            authority.Issuer + "/protocol/token",
            new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded"));
    }

    private static async Task<JsonElement> ExchangeAsync(IFakeOidcAuthority authority, string code)
    {
        var response = await PostAsync(
            authority,
            $"grant_type=authorization_code&code={code}&client_id=test-client&redirect_uri=https%3A%2F%2Flocalhost%2Fsignin-oidc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    /// <summary>A clock that stays where it was put.</summary>
    private sealed class StoppedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public StoppedClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
