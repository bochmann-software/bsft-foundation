using System.Text;
using Bsft.Testing.Tests.Support;
using Bsft.Testing.Web.Push;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Bsft.Testing.Tests.Web;

/// <summary>
/// The VAPID header, read the way a push service reads it.
/// </summary>
/// <remarks>
/// Every malformed header yields <see langword="null"/> rather than an exception, because a push
/// service answers 401 to one it cannot read. A fake that threw would turn "the sender built the
/// header wrongly" — the finding a test is after — into a failure inside the transport.
/// </remarks>
public sealed class FakePushVapidTests
{
    private static readonly DateTimeOffset Expiry = new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AWellFormedHeader_IsReadAndItsSignatureVerifies()
    {
        using var signer = new VapidTestSigner();

        var token = await ReadAsync(VapidTestSigner.Header(signer.Sign(Claims()), signer.PublicKey));

        token.Should().NotBeNull();
        token!.Audience.Should().Be(FakePushService.DefaultOrigin);
        token.Subject.Should().Be("mailto:ops@example.test");
        token.ExpiresAt.Should().Be(Expiry);
        token.PublicKey.Should().Be(signer.PublicKey);
        token.IsSignatureValid.Should().BeTrue();
    }

    [Fact]
    public async Task AHeaderSignedByAKeyOtherThanTheOneItDeclares_DoesNotVerify()
    {
        using var signer = new VapidTestSigner();
        using var impostor = new VapidTestSigner();

        // The one assertion a push service can actually make: the token was signed by whoever holds
        // the key the browser subscribed with. Publishing somebody else's key breaks exactly that.
        var token = await ReadAsync(VapidTestSigner.Header(impostor.Sign(Claims()), signer.PublicKey));

        token.Should().NotBeNull();
        token!.IsSignatureValid.Should().BeFalse();
    }

    [Fact]
    public async Task AHeaderThatIsAbsent_IsNoToken() =>
        (await ReadAsync(authorization: null)).Should().BeNull();

    [Fact]
    public async Task AHeaderOfAnotherScheme_IsNoToken() =>
        (await ReadAsync("Bearer an-access-token")).Should().BeNull();

    [Fact]
    public async Task AHeaderWithoutTheToken_IsNoToken()
    {
        using var signer = new VapidTestSigner();

        (await ReadAsync($"vapid k={signer.PublicKey}")).Should().BeNull();
    }

    [Fact]
    public async Task AHeaderWithoutTheKey_IsNoToken()
    {
        using var signer = new VapidTestSigner();

        (await ReadAsync($"vapid t={signer.Sign(Claims())}")).Should().BeNull();
    }

    [Fact]
    public async Task ATokenThatIsNotThreeParts_IsNoToken()
    {
        using var signer = new VapidTestSigner();

        (await ReadAsync(VapidTestSigner.Header("header.payload", signer.PublicKey))).Should().BeNull();
    }

    [Fact]
    public async Task ATokenWhosePayloadIsNotBase64Url_IsNoToken()
    {
        using var signer = new VapidTestSigner();

        (await ReadAsync(VapidTestSigner.Header(VapidTestSigner.TokenWithRawPayload("!!!not!!!"), signer.PublicKey)))
            .Should().BeNull();
    }

    [Fact]
    public async Task ATokenWhosePayloadIsNotJson_IsNoToken()
    {
        using var signer = new VapidTestSigner();

        (await ReadAsync(VapidTestSigner.Header(RawPayload("this is not json"), signer.PublicKey)))
            .Should().BeNull();
    }

    [Theory]
    [InlineData("""{"sub":"mailto:ops@example.test","exp":1786698000}""")]
    [InlineData("""{"aud":"https://push.fake.test","exp":1786698000}""")]
    [InlineData("""{"aud":"https://push.fake.test","sub":"mailto:ops@example.test"}""")]
    public async Task ATokenMissingAnyOfTheThreeClaims_IsNoToken(string claims)
    {
        using var signer = new VapidTestSigner();

        (await ReadAsync(VapidTestSigner.Header(signer.Sign(claims), signer.PublicKey))).Should().BeNull();
    }

    [Fact]
    public async Task ATokenWhoseAudienceAndSubjectAreNull_ReadsThemAsEmpty()
    {
        using var signer = new VapidTestSigner();

        var claims = $$"""{"aud":null,"sub":null,"exp":{{Expiry.ToUnixTimeSeconds()}}}""";

        var token = await ReadAsync(VapidTestSigner.Header(signer.Sign(claims), signer.PublicKey));

        token.Should().NotBeNull();
        token!.Audience.Should().BeEmpty();
        token.Subject.Should().BeEmpty();
    }

    [Fact]
    public async Task AKeyThatIsNotBase64Url_DoesNotVerify()
    {
        using var signer = new VapidTestSigner();

        var token = await ReadAsync(VapidTestSigner.Header(signer.Sign(Claims()), "!!!not-base64url!!!"));

        token.Should().NotBeNull();
        token!.IsSignatureValid.Should().BeFalse();
    }

    [Fact]
    public async Task AKeyThatIsNotAPoint_DoesNotVerify()
    {
        using var signer = new VapidTestSigner();

        var token = await ReadAsync(
            VapidTestSigner.Header(signer.Sign(Claims()), WebEncoders.Base64UrlEncode(new byte[64])));

        token.Should().NotBeNull();
        token!.IsSignatureValid.Should().BeFalse();
    }

    [Fact]
    public async Task AKeyOfTheRightLengthThatIsNotOnTheCurve_DoesNotVerify()
    {
        using var signer = new VapidTestSigner();

        // Well-formed and still not a key: the coordinates do not satisfy the curve equation, which
        // the cryptographic provider refuses rather than answering "false".
        var offCurve = new byte[65];
        offCurve[0] = 0x04;
        offCurve[64] = 0x01;

        var token = await ReadAsync(
            VapidTestSigner.Header(signer.Sign(Claims()), WebEncoders.Base64UrlEncode(offCurve)));

        token.Should().NotBeNull();
        token!.IsSignatureValid.Should().BeFalse();
    }

    [Fact]
    public async Task ASignatureThatIsNotBase64Url_DoesNotVerify()
    {
        using var signer = new VapidTestSigner();

        var token = await ReadAsync(
            VapidTestSigner.Header(
                VapidTestSigner.WithSignature(signer.Sign(Claims()), "!!!not-base64url!!!"),
                signer.PublicKey));

        token.Should().NotBeNull();
        token!.IsSignatureValid.Should().BeFalse();
    }

    private static string Claims() =>
        $$"""{"aud":"{{FakePushService.DefaultOrigin}}","sub":"mailto:ops@example.test","exp":{{Expiry.ToUnixTimeSeconds()}}}""";

    private static string RawPayload(string payload) =>
        VapidTestSigner.TokenWithRawPayload(WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payload)));

    private static async Task<FakeVapidToken?> ReadAsync(string? authorization)
    {
        using var service = new FakePushService();

        await FakePushDriver.SendAsync(
            service,
            WebPushEncryption.Encrypt("hello", service.Subscription),
            authorization: authorization);

        return service.Requests.Single().Vapid;
    }
}
