using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Bsft.Testing.Web;
using Bsft.Testing.Web.Oidc;
using FluentAssertions;
using Xunit;

namespace Bsft.Testing.Tests.Web.Oidc;

/// <summary>
/// The token endpoint's three grants and the quirk switches that make the fake authority behave like a
/// real-world provider rather than like the specification.
/// </summary>
/// <remarks>
/// The switches exist so an outbound client's quirk handling can be tested against an authority that
/// actually misbehaves. They are worth their own tests for the same reason any test harness is: a
/// harness that silently stopped honouring a switch would turn the suites built on it green for the
/// wrong reason.
/// </remarks>
public sealed class FakeOidcTokenEndpointTests
{
    [Fact]
    public async Task ARefreshGrant_IsHonouredWhenTheTokenIsOneThisAuthorityIssued()
    {
        using var authority = new FakeOidcAuthority { Policy = new FakeOidcTokenPolicy { IsRefreshTokenIssued = true } };
        using var client = Client(authority);

        var first = await ExchangeAsync(authority, client);
        var refreshToken = first.GetProperty("refresh_token").GetString();

        var renewed = await PostAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken!,
        });

        renewed.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        renewed.TryGetProperty("id_token", out _).Should().BeFalse("a renewal is not a fresh authentication");
    }

    [Fact]
    public async Task ARefreshGrant_IsRefusedForATokenThisAuthorityNeverIssued()
    {
        using var authority = new FakeOidcAuthority();
        using var client = Client(authority);

        var response = await SendAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = "invented",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task ARefreshGrantThatCarriesNoTokenAtAll_IsRefused()
    {
        using var authority = new FakeOidcAuthority();
        using var client = Client(authority);

        var response = await SendAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RevokingTheIssuedRefreshTokens_MakesTheNextRenewalFail()
    {
        using var authority = new FakeOidcAuthority { Policy = new FakeOidcTokenPolicy { IsRefreshTokenIssued = true } };
        using var client = Client(authority);

        var granted = await ExchangeAsync(authority, client);
        var refreshToken = granted.GetProperty("refresh_token").GetString()!;

        authority.ValidRefreshTokens.Should().Contain(refreshToken);

        authority.RevokeIssuedRefreshTokens();

        authority.ValidRefreshTokens.Should().BeEmpty();

        var response = await SendAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WithRotation_ARefreshTokenIsSpentByTheRenewalThatUsesIt()
    {
        using var authority = new FakeOidcAuthority
        {
            Policy = new FakeOidcTokenPolicy { IsRefreshTokenIssued = true, AreRefreshTokensRotated = true },
        };

        using var client = Client(authority);

        var granted = await ExchangeAsync(authority, client);
        var first = granted.GetProperty("refresh_token").GetString()!;

        var renewed = await PostAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = first,
        });

        renewed.GetProperty("refresh_token").GetString().Should().NotBe(first);
        authority.ValidRefreshTokens.Should().NotContain(first);
    }

    [Fact]
    public async Task EveryRefreshGrant_IsRefusedWhenThePolicySaysSo()
    {
        using var authority = new FakeOidcAuthority
        {
            Policy = new FakeOidcTokenPolicy { IsRefreshTokenIssued = true, IsRefreshGrantRejected = true },
        };

        using var client = Client(authority);
        var granted = await ExchangeAsync(authority, client);

        var response = await SendAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = granted.GetProperty("refresh_token").GetString()!,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AClientCredentialsGrant_IssuesATokenWithoutAnyAuthorizationCode()
    {
        using var authority = new FakeOidcAuthority();
        using var client = Client(authority);

        var granted = await PostAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
        });

        granted.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        granted.TryGetProperty("id_token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task EveryGrant_IsRefusedWhenThePolicySaysSo()
    {
        using var authority = new FakeOidcAuthority { Policy = new FakeOidcTokenPolicy { IsEveryGrantRejected = true } };
        using var client = Client(authority);

        var response = await SendAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
        });

        (await ReadAsync(response)).GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task AForcedStatus_IsAnsweredWhateverTheRequestSays()
    {
        using var authority = new FakeOidcAuthority
        {
            Policy = new FakeOidcTokenPolicy { ForcedTokenStatus = HttpStatusCode.ServiceUnavailable },
        };

        using var client = Client(authority);

        var response = await SendAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
        });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task AnAuthorityInsistingOnBasicAuthentication_RefusesCredentialsInTheBody()
    {
        using var authority = new FakeOidcAuthority
        {
            Policy = new FakeOidcTokenPolicy { IsBasicAuthenticationRequired = true },
        };

        using var client = Client(authority);

        var response = await SendAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "client",
            ["client_secret"] = "secret",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadAsync(response)).GetProperty("error").GetString().Should().Be("invalid_client");
    }

    [Fact]
    public async Task AnAuthorityInsistingOnBodyCredentials_RefusesABasicHeader()
    {
        using var authority = new FakeOidcAuthority
        {
            Policy = new FakeOidcTokenPolicy { AreCredentialsRequiredInRequestBody = true },
        };

        using var client = Client(authority);

        var response = await SendAsync(
            client,
            authority,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["grant_type"] = "client_credentials" },
            new AuthenticationHeaderValue("Basic", "Y2xpZW50OnNlY3JldA=="));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnAuthorityThatPredatesTheProofKey_RefusesARequestCarryingAVerifier()
    {
        using var authority = new FakeOidcAuthority { Policy = new FakeOidcTokenPolicy { IsProofKeyRejected = true } };
        using var client = Client(authority);

        var response = await SendAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = "a-verifier",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("error").GetString().Should().Be("invalid_request");
    }

    [Fact]
    public async Task AnAuthorityThatPublishesNoLifetime_OmitsTheField()
    {
        using var authority = new FakeOidcAuthority { Policy = new FakeOidcTokenPolicy { IsExpiresInOmitted = true } };
        using var client = Client(authority);

        var granted = await PostAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
        });

        granted.TryGetProperty("expires_in", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AnAuthorityThatWritesTheLifetimeAsAString_DoesSo()
    {
        using var authority = new FakeOidcAuthority
        {
            Policy = new FakeOidcTokenPolicy { IsExpiresInWrittenAsAString = true, AccessTokenLifetime = TimeSpan.FromMinutes(2) },
        };

        using var client = Client(authority);

        var granted = await PostAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
        });

        granted.GetProperty("expires_in").ValueKind.Should().Be(JsonValueKind.String);
        granted.GetProperty("expires_in").GetString().Should().Be("120");
    }

    [Fact]
    public async Task AnAuthorityReportingAGrantedScope_PublishesIt()
    {
        using var authority = new FakeOidcAuthority
        {
            Policy = new FakeOidcTokenPolicy { GrantedScope = "accounting.read" },
        };

        using var client = Client(authority);

        var granted = await PostAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
        });

        granted.GetProperty("scope").GetString().Should().Be("accounting.read");
    }

    [Fact]
    public async Task TheRevocationEndpoint_AcknowledgesAndCanBeMadeToRefuse()
    {
        using var authority = new FakeOidcAuthority();
        using var client = Client(authority);

        var acknowledged = await client.PostAsync(
            new Uri(authority.Issuer + "/protocol/revoke"), new FormUrlEncodedContent(new Dictionary<string, string>()));

        acknowledged.StatusCode.Should().Be(HttpStatusCode.OK);

        authority.Policy = authority.Policy with { IsRevocationRefused = true };

        var refused = await client.PostAsync(
            new Uri(authority.Issuer + "/protocol/revoke"), new FormUrlEncodedContent(new Dictionary<string, string>()));

        refused.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task TheCheapAuthorizedEndpoint_NeedsABearerCredential()
    {
        using var authority = new FakeOidcAuthority();
        using var client = Client(authority);

        var anonymous = await client.GetAsync(new Uri(authority.Issuer + "/api/ping"));
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var authorized = new HttpRequestMessage(HttpMethod.Get, new Uri(authority.Issuer + "/api/ping"));
        authorized.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "a-token");

        (await client.SendAsync(authorized)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheDiscoveryDocument_NamesTheRevocationEndpointAndEveryGrantItServes()
    {
        using var authority = new FakeOidcAuthority();
        using var client = Client(authority);

        var document = await ReadAsync(
            await client.GetAsync(new Uri(authority.Issuer + "/.well-known/openid-configuration")));

        document.GetProperty("revocation_endpoint").GetString().Should().StartWith(authority.Issuer);
        document.GetProperty("grant_types_supported").EnumerateArray().Select(entry => entry.GetString())
            .Should().BeEquivalentTo("authorization_code", "refresh_token", "client_credentials");
    }

    [Fact]
    public async Task ARefusedRequest_IsRecordedJustLikeAnAcceptedOne()
    {
        using var authority = new FakeOidcAuthority { Policy = new FakeOidcTokenPolicy { IsEveryGrantRejected = true } };
        using var client = Client(authority);

        await SendAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "client",
            ["client_secret"] = "secret",
            ["scope"] = "a b",
        });

        var recorded = authority.TokenRequests.Should().ContainSingle().Subject;
        recorded.GrantType.Should().Be("client_credentials");
        recorded.ClientSecret.Should().Be("secret");
        recorded.Scope.Should().Be("a b");
        recorded.HasBasicAuthentication.Should().BeFalse();
    }

    [Fact]
    public async Task ATokenRequestWithNoBodyAtAll_IsHandledRatherThanThrowing()
    {
        using var authority = new FakeOidcAuthority();
        using var client = Client(authority);

        var response = await client.PostAsync(new Uri(authority.Issuer + "/protocol/token"), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static HttpClient Client(FakeOidcAuthority authority) => new(authority.CreateBackchannelHandler());

    private static async Task<JsonElement> ExchangeAsync(FakeOidcAuthority authority, HttpClient client)
    {
        var code = authority.IssueCode(new FakeOidcAuthorizationRequest(new FakeOidcUser("admin")));

        return await PostAsync(client, authority, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = "client",
        });
    }

    private static async Task<JsonElement> PostAsync(
        HttpClient client, FakeOidcAuthority authority, Dictionary<string, string> form)
        => await ReadAsync(await SendAsync(client, authority, form));

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        FakeOidcAuthority authority,
        Dictionary<string, string> form,
        AuthenticationHeaderValue? authorization = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(authority.Issuer + "/protocol/token"))
        {
            Content = new FormUrlEncodedContent(form),
        };

        request.Headers.Authorization = authorization;

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}
