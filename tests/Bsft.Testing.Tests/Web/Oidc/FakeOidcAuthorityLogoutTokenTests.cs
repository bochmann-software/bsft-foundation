using Bsft.Testing.Web;
using Bsft.Testing.Web.Oidc;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Bsft.Testing.Tests.Web.Oidc;

/// <summary>
/// The fake authority's logout-token half: the token a receiver must accept, and the eight it must
/// refuse.
/// </summary>
/// <remarks>
/// A test double whose negative cases are wrong is worse than no double at all — it would let a
/// receiver pass while accepting tokens a real authority never mints. So each defect is asserted here
/// against the token itself, independently of any receiver's opinion of it.
/// </remarks>
public class FakeOidcAuthorityLogoutTokenTests
{
    private const string Audience = "rp-1";

    [Fact]
    public void A_clean_logout_token_carries_what_back_channel_logout_is_defined_by()
    {
        using var authority = new FakeOidcAuthority();

        var token = new JsonWebToken(authority.CreateLogoutToken(Audience, subject: "user-1", sessionId: "session-1"));

        token.Issuer.Should().Be(authority.Issuer);
        token.Audiences.Should().Equal(Audience);
        token.GetClaim("sub").Value.Should().Be("user-1");
        token.GetClaim("sid").Value.Should().Be("session-1");
        token.Id.Should().NotBeNullOrWhiteSpace();
        token.TryGetPayloadValue<object>("nonce", out _).Should().BeFalse();
        Events(token).TryGetProperty("http://schemas.openid.net/event/backchannel-logout", out _).Should().BeTrue();
    }

    [Fact]
    public void A_logout_token_is_signed_under_the_key_the_published_key_set_names()
    {
        using var authority = new FakeOidcAuthority();

        // The property the specification relies on: a relying party that already trusts this authority
        // needs no second trust anchor for logout.
        new JsonWebToken(authority.CreateLogoutToken(Audience, sessionId: "session-1")).Kid
            .Should().Be("fake-oidc-key");
    }

    [Fact]
    public void A_token_naming_only_a_session_omits_the_subject_and_the_other_way_round()
    {
        using var authority = new FakeOidcAuthority();

        var sessionOnly = new JsonWebToken(authority.CreateLogoutToken(Audience, sessionId: "session-1"));
        sessionOnly.TryGetPayloadValue<string>("sub", out _).Should().BeFalse();

        var subjectOnly = new JsonWebToken(authority.CreateLogoutToken(Audience, subject: "user-1"));
        subjectOnly.TryGetPayloadValue<string>("sid", out _).Should().BeFalse();
    }

    [Fact]
    public void A_foreign_signing_key_produces_a_token_this_authority_did_not_sign()
    {
        using var authority = new FakeOidcAuthority();

        var defective = new JsonWebToken(Defective(authority, FakeLogoutTokenDefect.ForeignSigningKey));
        var genuine = new JsonWebToken(authority.CreateLogoutToken(Audience, sessionId: "session-1"));

        // Deliberately the same kid as the genuine one. A receiver that resolves the key by kid and
        // stops there would accept this, which is exactly the mistake the defect exists to catch.
        defective.Kid.Should().Be(genuine.Kid);
        defective.EncodedSignature.Should().NotBe(genuine.EncodedSignature);
    }

    [Fact]
    public void The_wrong_audience_names_someone_other_than_the_relying_party()
    {
        using var authority = new FakeOidcAuthority();

        new JsonWebToken(Defective(authority, FakeLogoutTokenDefect.WrongAudience)).Audiences
            .Should().NotContain(Audience);
    }

    [Fact]
    public void An_unknown_issuer_names_an_authority_the_relying_party_never_configured()
    {
        using var authority = new FakeOidcAuthority();

        new JsonWebToken(Defective(authority, FakeLogoutTokenDefect.UnknownIssuer)).Issuer
            .Should().NotBe(authority.Issuer);
    }

    [Fact]
    public void A_stale_token_is_minted_far_enough_back_to_fall_outside_any_sane_window()
    {
        using var authority = new FakeOidcAuthority();

        var stale = new JsonWebToken(Defective(authority, FakeLogoutTokenDefect.StaleIssuedAt));

        stale.IssuedAt.Should().BeBefore(authority.Clock.GetUtcNow().UtcDateTime.AddMinutes(-10));
    }

    [Fact]
    public void A_nonce_is_present_only_when_the_defect_asks_for_one()
    {
        using var authority = new FakeOidcAuthority();

        // A nonce is how a receiver tells a logout token from an id_token, so a token carrying one must
        // be refused.
        new JsonWebToken(Defective(authority, FakeLogoutTokenDefect.NoncePresent))
            .TryGetPayloadValue<string>("nonce", out _).Should().BeTrue();
    }

    [Fact]
    public void The_events_claim_can_be_missing_altogether_or_carry_the_wrong_key()
    {
        using var authority = new FakeOidcAuthority();

        new JsonWebToken(Defective(authority, FakeLogoutTokenDefect.EventsMissing))
            .TryGetPayloadValue<object>("events", out _).Should().BeFalse();

        Events(new JsonWebToken(Defective(authority, FakeLogoutTokenDefect.EventsWrongKey)))
            .TryGetProperty("http://schemas.openid.net/event/backchannel-logout", out _).Should().BeFalse();
    }

    [Fact]
    public void A_token_naming_neither_a_session_nor_a_subject_asks_the_receiver_to_end_nothing()
    {
        using var authority = new FakeOidcAuthority();

        var token = new JsonWebToken(authority.CreateLogoutToken(
            new FakeLogoutTokenRequest(Audience, "user-1", "session-1", FakeLogoutTokenDefect.NoSubjectAndNoSessionId)));

        token.TryGetPayloadValue<string>("sub", out _).Should().BeFalse();
        token.TryGetPayloadValue<string>("sid", out _).Should().BeFalse();
    }

    [Fact]
    public void The_two_claims_a_receiver_cannot_do_without_can_each_be_left_out()
    {
        using var authority = new FakeOidcAuthority();

        // Without a jti a receiver has no key to catch a replay by; without an iat it cannot judge
        // freshness. Both are refusals rather than tolerances, so both need a token to refuse.
        new JsonWebToken(Defective(authority, FakeLogoutTokenDefect.JwtIdMissing))
            .TryGetPayloadValue<string>("jti", out _).Should().BeFalse();

        new JsonWebToken(Defective(authority, FakeLogoutTokenDefect.IssuedAtMissing))
            .TryGetPayloadValue<long>("iat", out _).Should().BeFalse();
    }

    [Fact]
    public void Minting_refuses_a_request_that_names_no_relying_party()
    {
        using var authority = new FakeOidcAuthority();

        var missing = () => authority.CreateLogoutToken(null!);
        missing.Should().Throw<ArgumentNullException>().WithParameterName("request");

        var blank = () => authority.CreateLogoutToken(new FakeLogoutTokenRequest(" ", null, "session-1"));
        blank.Should().Throw<ArgumentException>().WithParameterName("request");
    }

    [Fact]
    public async Task Delivery_posts_the_token_form_encoded_under_the_field_the_specification_fixes()
    {
        using var authority = new FakeOidcAuthority();
        using var receiver = new RecordingReceiver();
        using var client = new HttpClient(receiver) { BaseAddress = new Uri("https://rp.example") };

        await authority.SendBackChannelLogoutAsync(
            client,
            "/auth/oidc/backchannel-logout",
            new FakeLogoutTokenRequest(Audience, null, "session-1"));

        // A receiver that only accepted JSON would pass a hand-rolled test and fail every real
        // authority.
        receiver.ContentType.Should().Be("application/x-www-form-urlencoded");
        receiver.Body.Should().StartWith("logout_token=");
    }

    [Fact]
    public async Task The_same_minted_token_can_be_delivered_twice_so_a_replay_can_be_tested()
    {
        using var authority = new FakeOidcAuthority();
        using var receiver = new RecordingReceiver();
        using var client = new HttpClient(receiver) { BaseAddress = new Uri("https://rp.example") };

        var token = authority.CreateLogoutToken(Audience, sessionId: "session-1");

        await FakeOidcAuthority.SendLogoutTokenAsync(client, "/auth/oidc/backchannel-logout", token);
        await FakeOidcAuthority.SendLogoutTokenAsync(client, "/auth/oidc/backchannel-logout", token);

        receiver.Posts.Should().Be(2);
    }

    [Fact]
    public async Task Delivery_refuses_a_missing_client_a_missing_path_or_a_missing_token()
    {
        using var authority = new FakeOidcAuthority();
        using var receiver = new RecordingReceiver();
        using var client = new HttpClient(receiver) { BaseAddress = new Uri("https://rp.example") };
        var request = new FakeLogoutTokenRequest(Audience, null, "session-1");

        var noClient = () => authority.SendBackChannelLogoutAsync(null!, "/path", request);
        await noClient.Should().ThrowAsync<ArgumentNullException>().WithParameterName("relyingParty");

        var noPath = () => authority.SendBackChannelLogoutAsync(client, null!, request);
        await noPath.Should().ThrowAsync<ArgumentNullException>().WithParameterName("endpointPath");

        var noRawClient = () => FakeOidcAuthority.SendLogoutTokenAsync(null!, "/path", "token");
        await noRawClient.Should().ThrowAsync<ArgumentNullException>().WithParameterName("relyingParty");

        var noRawPath = () => FakeOidcAuthority.SendLogoutTokenAsync(client, null!, "token");
        await noRawPath.Should().ThrowAsync<ArgumentNullException>().WithParameterName("endpointPath");

        var noToken = () => FakeOidcAuthority.SendLogoutTokenAsync(client, "/path", null!);
        await noToken.Should().ThrowAsync<ArgumentNullException>().WithParameterName("logoutToken");
    }

    private static string Defective(FakeOidcAuthority authority, FakeLogoutTokenDefect defect)
        => authority.CreateLogoutToken(new FakeLogoutTokenRequest(Audience, "user-1", "session-1", defect));

    private static System.Text.Json.JsonElement Events(JsonWebToken token)
        => token.GetPayloadValue<System.Text.Json.JsonElement>("events");

    /// <summary>A relying party that records the POST rather than answering it for real.</summary>
    private sealed class RecordingReceiver : HttpMessageHandler
    {
        internal int Posts { get; private set; }

        internal string? ContentType { get; private set; }

        internal string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Posts++;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }
}
