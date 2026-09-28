using Bsft.Core;
using Bsft.Idempotency.Fingerprinting;
using Bsft.Idempotency.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using System.Text;
using Xunit;

namespace Bsft.Idempotency.Tests.Fingerprinting;

public class RequestBodyDigestTests
{
    [Fact]
    public async Task ComputeAsync_TheSameBody_DigestsToTheSameSixtyFourHexCharacters()
    {
        var first = await RequestBodyDigest.ComputeAsync(HttpContexts.Create(body: "{\"a\":1}").Request, 1024);
        var second = await RequestBodyDigest.ComputeAsync(HttpContexts.Create(body: "{\"a\":1}").Request, 1024);

        first.Should().Be(second);
        first.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task ComputeAsync_ADifferentBody_DigestsDifferently()
    {
        var first = await RequestBodyDigest.ComputeAsync(HttpContexts.Create(body: "{\"a\":1}").Request, 1024);
        var second = await RequestBodyDigest.ComputeAsync(HttpContexts.Create(body: "{\"a\":2}").Request, 1024);

        first.Should().NotBe(second);
    }

    [Fact]
    public async Task ComputeAsync_AfterwardsTheEndpointStillReadsTheBodyItWasSent()
    {
        var context = HttpContexts.Create(body: "{\"amount\":10}");

        await RequestBodyDigest.ComputeAsync(context.Request, 1024);

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);

        (await reader.ReadToEndAsync()).Should().Be("{\"amount\":10}");
    }

    [Fact]
    public async Task ComputeAsync_AnEmptyBody_IsStillDigested()
    {
        var digest = await RequestBodyDigest.ComputeAsync(HttpContexts.Create(body: string.Empty).Request, 1024);

        digest.Should().HaveLength(64);
    }

    [Fact]
    public async Task ComputeAsync_ABodyExactlyAtTheBound_IsRead()
    {
        var context = HttpContexts.Create(body: new string('x', 16));

        var digest = await RequestBodyDigest.ComputeAsync(context.Request, 16);

        digest.Should().HaveLength(64);
    }

    [Fact]
    public async Task ComputeAsync_ADeclaredLengthOverTheBound_IsRefusedWithoutReadingAByte()
    {
        var context = HttpContexts.Create(body: string.Empty);

        context.Request.ContentLength = 4096;

        var thrown = await FluentActions.Awaiting(() => RequestBodyDigest.ComputeAsync(context.Request, 16).AsTask())
            .Should().ThrowAsync<PayloadTooLargeException>();

        thrown.Which.ErrorCode.Should().Be(IdempotencyDefaults.PayloadTooLargeErrorCode);
        thrown.Which.Message.Should().Contain("16 bytes");
    }

    [Fact]
    public async Task ComputeAsync_AStreamedBodyThatOutgrowsTheBound_IsRefusedWhileItIsRead()
    {
        var context = HttpContexts.Create(body: new string('x', 64));

        // No declared length, so the cheap precheck cannot settle it and the read has to.
        context.Request.ContentLength = null;

        await FluentActions.Awaiting(() => RequestBodyDigest.ComputeAsync(context.Request, 16).AsTask())
            .Should().ThrowAsync<PayloadTooLargeException>();
    }

    [Fact]
    public async Task ComputeAsync_ABodyLongerThanOneReadBuffer_IsDigestedWhole()
    {
        var context = HttpContexts.Create(body: new string('x', 20_000));

        var digest = await RequestBodyDigest.ComputeAsync(context.Request, 1024 * 1024);

        digest.Should().HaveLength(64);
    }

    [Fact]
    public async Task ComputeAsync_NullRequest_IsRefused() =>
        await FluentActions.Awaiting(() => RequestBodyDigest.ComputeAsync(null!, 16).AsTask())
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
}
