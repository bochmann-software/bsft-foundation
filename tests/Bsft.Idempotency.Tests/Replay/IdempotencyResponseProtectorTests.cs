using System.Security.Cryptography;
using System.Text;
using Bsft.Idempotency.Replay;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;
using Xunit;

namespace Bsft.Idempotency.Tests.Replay;

public class IdempotencyResponseProtectorTests
{
    private const string Scope = "scope-1";
    private const string Key = "key-1";

    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("{\"client_secret\":\"s3cr3t-value\"}");

    [Fact]
    public void Protect_ThenFindReplayableBody_GivesBackTheBodyAndStoresNoPlaintext()
    {
        var protector = Protector();

        var stored = protector.Protect(Secret, Scope, Key)!;

        Encoding.UTF8.GetString(stored).Should().NotContain("s3cr3t-value");
        protector.FindReplayableBody(stored, isSensitive: true, Scope, Key).Should().Equal(Secret);
    }

    [Fact]
    public void FindReplayableBody_SealedBodyOnAnEndpointNoLongerMarked_IsStillUnsealed()
    {
        var protector = Protector();

        protector.FindReplayableBody(protector.Protect(Secret, Scope, Key)!, isSensitive: false, Scope, Key).Should().Equal(Secret);
    }

    [Fact]
    public void FindReplayableBody_PlainBodyOnAnOrdinaryEndpoint_IsReplayedAsStored()
    {
        var stored = Encoding.UTF8.GetBytes("{\"id\":1}");

        Protector().FindReplayableBody(stored, isSensitive: false, Scope, Key).Should().BeSameAs(stored);
    }

    [Fact]
    public void FindReplayableBody_PlainBodyOnASensitiveEndpoint_IsWithheld()
        => Protector().FindReplayableBody(Secret, isSensitive: true, Scope, Key).Should().BeNull();

    [Fact]
    public void FindReplayableBody_BodySealedWithAnotherKeyRing_IsWithheld()
    {
        var stored = Protector().Protect(Secret, Scope, Key)!;

        Protector().FindReplayableBody(stored, isSensitive: true, Scope, Key).Should().BeNull();
    }

    [Theory]
    [InlineData("scope-2", Key)]
    [InlineData(Scope, "key-2")]
    public void FindReplayableBody_BodySealedForAnotherRecord_IsWithheld(string scope, string key)
    {
        var protector = Protector();

        protector.FindReplayableBody(protector.Protect(Secret, Scope, Key)!, isSensitive: true, scope, key).Should().BeNull();
    }

    [Fact]
    public void Protect_NullScope_IsRefused() =>
        FluentActions.Invoking(() => Protector().Protect(Secret, null!, Key))
            .Should().Throw<ArgumentNullException>().WithParameterName("scope");

    [Fact]
    public void FindReplayableBody_NullKey_IsRefused() =>
        FluentActions.Invoking(() => Protector().FindReplayableBody(Secret, true, Scope, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("key");

    [Fact]
    public void Protect_KeyRingThatCannotSeal_StoresNothing()
    {
        var dataProtector = Substitute.For<IDataProtector>();
        dataProtector.Protect(Arg.Any<byte[]>()).Returns(_ => throw new CryptographicException("no key"));
        dataProtector.CreateProtector(Arg.Any<string>()).Returns(dataProtector);
        var provider = Substitute.For<IDataProtectionProvider>();
        provider.CreateProtector(IdempotencyResponseProtector.Purpose).Returns(dataProtector);

        new IdempotencyResponseProtector(provider).Protect(Secret, Scope, Key).Should().BeNull();
    }

    [Fact]
    public void Constructor_NullProvider_IsRefused() =>
        FluentActions.Invoking(() => new IdempotencyResponseProtector(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("provider");

    [Fact]
    public void Protect_NullBody_IsRefused() =>
        FluentActions.Invoking(() => Protector().Protect(null!, Scope, Key))
            .Should().Throw<ArgumentNullException>().WithParameterName("body");

    [Fact]
    public void FindReplayableBody_NullBody_IsRefused() =>
        FluentActions.Invoking(() => Protector().FindReplayableBody(null!, true, Scope, Key))
            .Should().Throw<ArgumentNullException>().WithParameterName("storedBody");

    private static IdempotencyResponseProtector Protector() => new(new EphemeralDataProtectionProvider());
}
