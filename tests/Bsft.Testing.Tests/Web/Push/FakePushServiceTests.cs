using System.Net;
using Bsft.Testing.Tests.Support;
using Bsft.Testing.Web.Push;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Bsft.Testing.Tests.Web.Push;

/// <summary>
/// The subscription the fake hands out, and the answers it gives a sender.
/// </summary>
/// <remarks>
/// The subscription is asserted on in the browser's own terms — 65 bytes beginning 0x04, 16 bytes of
/// secret — because that is what makes it postable at a registration endpoint unchanged. A fake whose
/// keys were merely plausible would be rejected by every validator worth having, and the suites built
/// on it would be testing the rejection.
/// </remarks>
public sealed class FakePushServiceTests
{
    [Fact]
    public void Subscription_IsWhatABrowserWouldHaveProduced()
    {
        using var service = new FakePushService();

        var point = WebEncoders.Base64UrlDecode(service.Subscription.P256dh);

        point.Should().HaveCount(65);
        point[0].Should().Be(0x04, "a browser publishes the uncompressed point");
        WebEncoders.Base64UrlDecode(service.Subscription.Auth).Should().HaveCount(16);
        service.Subscription.Endpoint.Should().StartWith(FakePushService.DefaultOrigin + "/");
    }

    [Fact]
    public void Subscription_WithAnEndpointGiven_CarriesItVerbatim()
    {
        const string endpoint = "https://fcm.example.test/wp/registration-42";

        using var service = new FakePushService(endpoint);

        service.Subscription.Endpoint.Should().Be(endpoint);
    }

    [Fact]
    public void TwoServices_AreTwoDifferentBrowsers()
    {
        using var first = new FakePushService();
        using var second = new FakePushService();

        // A test that registers two browsers for one recipient needs them to be genuinely two, down to
        // the key material: sharing it would hide a sender that pushed twice to the same subscription.
        second.Subscription.Endpoint.Should().NotBe(first.Subscription.Endpoint);
        second.Subscription.P256dh.Should().NotBe(first.Subscription.P256dh);
        second.Subscription.Auth.Should().NotBe(first.Subscription.Auth);
    }

    [Fact]
    public async Task APush_IsRecordedWithItsHeadersAndItsPayloadDecrypted()
    {
        using var service = new FakePushService();

        var response = await FakePushDriver.SendAsync(
            service,
            WebPushEncryption.Encrypt("Your invoice is overdue.", service.Subscription));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var recorded = service.Requests.Should().ContainSingle().Subject;

        recorded.Endpoint.Should().Be(new Uri(service.Subscription.Endpoint));
        recorded.TimeToLiveSeconds.Should().Be(60);
        recorded.ContentEncoding.Should().Be("aes128gcm");
        recorded.Vapid.Should().BeNull("this push carried no Authorization header");
        recorded.Payload.Should().Be("Your invoice is overdue.");
    }

    [Fact]
    public async Task APush_CarryingTextThatIsNotAscii_RoundTripsAsUtf8()
    {
        const string payload = "Rechnung für Müllers Straßenbau ist überfällig – 1 234,56 €";

        using var service = new FakePushService();

        await FakePushDriver.SendAsync(service, WebPushEncryption.Encrypt(payload, service.Subscription));

        // The plaintext is bytes all the way through, so a decryptor that reached for ASCII anywhere
        // would still authenticate and still hand back the wrong text.
        service.Requests.Single().Payload.Should().Be(payload);
    }

    [Fact]
    public async Task EveryPush_IsRecordedInTheOrderItArrived()
    {
        using var service = new FakePushService();

        await FakePushDriver.SendAsync(service, WebPushEncryption.Encrypt("first", service.Subscription));
        await FakePushDriver.SendAsync(service, WebPushEncryption.Encrypt("second", service.Subscription));

        service.Requests.Select(request => request.Payload).Should().Equal("first", "second");
    }

    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task TheService_AnswersWhateverStatusItWasGiven(HttpStatusCode status)
    {
        using var service = new FakePushService { Status = status };

        var response = await FakePushDriver.SendAsync(
            service,
            WebPushEncryption.Encrypt("hello", service.Subscription));

        response.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task APush_IsRecordedEvenWhenTheServiceReportsTheBrowserGone()
    {
        using var service = new FakePushService { Status = HttpStatusCode.Gone };

        await FakePushDriver.SendAsync(service, WebPushEncryption.Encrypt("hello", service.Subscription));

        // A real push service reads the request before it decides. A test asserting on what a sender
        // put on the wire must not have to make the answer a success to see it.
        service.Requests.Should().ContainSingle().Which.Payload.Should().Be("hello");
    }

    [Fact]
    public async Task ARetryAfter_IsSentWithA429()
    {
        using var service = new FakePushService
        {
            Status = HttpStatusCode.TooManyRequests,
            RetryAfter = TimeSpan.FromSeconds(90),
        };

        var response = await FakePushDriver.SendAsync(
            service,
            WebPushEncryption.Encrypt("hello", service.Subscription));

        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public async Task A429WithNoRetryAfterSet_CarriesNoHeader()
    {
        using var service = new FakePushService { Status = HttpStatusCode.TooManyRequests };

        var response = await FakePushDriver.SendAsync(
            service,
            WebPushEncryption.Encrypt("hello", service.Subscription));

        response.Headers.RetryAfter.Should().BeNull();
    }

    [Fact]
    public async Task ARetryAfter_IsNotSentWithAnythingButA429()
    {
        using var service = new FakePushService
        {
            Status = HttpStatusCode.RequestEntityTooLarge,
            RetryAfter = TimeSpan.FromSeconds(90),
        };

        var response = await FakePushDriver.SendAsync(
            service,
            WebPushEncryption.Encrypt("hello", service.Subscription));

        // Backing off after a 413 would be a sender bug, so the fake must never offer the excuse.
        response.Headers.RetryAfter.Should().BeNull();
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData(null, null)]
    [InlineData("as soon as you can", null)]
    public async Task TheTimeToLive_IsReadOnlyWhenItIsANumber(string? header, int? expected)
    {
        using var service = new FakePushService();

        await FakePushDriver.SendAsync(
            service,
            WebPushEncryption.Encrypt("hello", service.Subscription),
            timeToLive: header);

        service.Requests.Single().TimeToLiveSeconds.Should().Be(expected);
    }

    [Fact]
    public async Task APushDeclaringNoContentEncoding_RecordsNone()
    {
        using var service = new FakePushService();

        await FakePushDriver.SendAsync(
            service,
            WebPushEncryption.Encrypt("hello", service.Subscription),
            contentEncoding: null);

        service.Requests.Single().ContentEncoding.Should().BeNull();
    }

    [Fact]
    public async Task APushWithNoContentAtAll_FailsInTheDecryptorRatherThanBeingRecordedEmpty()
    {
        using var service = new FakePushService();

        // Documenting what the fake actually does: an empty body is not a push with an empty payload,
        // it is a body that carries no aes128gcm header, and the decryptor says so before anything is
        // recorded.
        await FluentActions.Awaiting(() => FakePushDriver.SendAsync(service, body: null))
            .Should().ThrowAsync<InvalidOperationException>();

        service.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Dispose_IsSafeToCallMoreThanOnce()
    {
        var service = new FakePushService();

        service.Dispose();

        FluentActions.Invoking(service.Dispose).Should().NotThrow();
        service.Subscription.Endpoint.Should().StartWith(FakePushService.DefaultOrigin);
    }
}
