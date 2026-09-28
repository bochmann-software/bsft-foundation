using System.Security.Cryptography;
using Bsft.Testing.Tests.Support;
using Bsft.Testing.Web.Push;
using FluentAssertions;
using Xunit;

namespace Bsft.Testing.Tests.Web;

/// <summary>
/// What the fake does with a body that is not the single well-formed <c>aes128gcm</c> record it expects.
/// </summary>
/// <remarks>
/// These are the failures worth having. A fake that quietly recorded an unreadable body would let a
/// sender with broken framing or a broken key derivation pass every test written against it, which is
/// precisely the class of bug nothing else catches.
/// </remarks>
public sealed class FakePushDecryptionTests
{
    /// <summary>16 bytes of salt, four of record size, one of key-id length.</summary>
    private const int FixedHeaderLength = 21;

    /// <summary>The uncompressed P-256 point the header carries as its key id.</summary>
    private const int KeyIdLength = 65;

    [Fact]
    public async Task ABodyShorterThanTheHeader_IsRefused()
    {
        using var service = new FakePushService();

        await FluentActions.Awaiting(() => FakePushDriver.SendAsync(service, new byte[FixedHeaderLength]))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*shorter than an aes128gcm header*");
    }

    [Fact]
    public async Task ABodyWhoseKeyIdIsNotAPoint_IsRefused()
    {
        using var service = new FakePushService();

        var body = WebPushEncryption.Encrypt("hello", service.Subscription);
        body[FixedHeaderLength - 1] = KeyIdLength - 1;

        // Anything but 65 there means the sender put something other than an uncompressed P-256 point
        // in the header, and there is then nothing to agree a key with.
        await FluentActions.Awaiting(() => FakePushDriver.SendAsync(service, body))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*uncompressed P-256 point*");
    }

    [Fact]
    public async Task ABodyWithNoRoomForARecordAndItsTag_IsRefused()
    {
        using var service = new FakePushService();

        // A well-formed header followed by exactly a tag's worth of bytes: the key id is a point, so
        // the length check is the only thing standing between this and an underflow in the unsealing.
        var truncated = WebPushEncryption.Encrypt("hello", service.Subscription)[..(FixedHeaderLength + KeyIdLength + 16)];

        await FluentActions.Awaiting(() => FakePushDriver.SendAsync(service, truncated))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ABodyWhoseCiphertextWasTamperedWith_DoesNotAuthenticate()
    {
        using var service = new FakePushService();

        var body = WebPushEncryption.Encrypt("Your invoice is overdue.", service.Subscription);
        body[^5] ^= 0xFF;

        // The tag is the whole point: one flipped bit anywhere in the record fails here rather than
        // producing a payload a test would have compared against something.
        await FluentActions.Awaiting(() => FakePushDriver.SendAsync(service, body))
            .Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task ABodyEncryptedToAnotherBrowser_DoesNotAuthenticate()
    {
        using var service = new FakePushService();
        using var somebodyElse = new FakePushService();

        var body = WebPushEncryption.Encrypt("hello", somebodyElse.Subscription);

        await FluentActions.Awaiting(() => FakePushDriver.SendAsync(service, body))
            .Should().ThrowAsync<CryptographicException>();
    }
}
