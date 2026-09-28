using System.Net;
using FluentAssertions;
using Xunit;

namespace Bsft.Http.Egress.Tests;

public class SystemEgressAddressResolverTests
{
    private readonly IEgressAddressResolver _resolver = new SystemEgressAddressResolver();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_ABlankHost_AnswersWithNothing(string host) =>
        (await _resolver.ResolveAsync(host)).Should().BeEmpty();

    [Fact]
    public async Task ResolveAsync_ALiteralAddress_IsHandedBackUnresolved()
    {
        // Not a resolution at all: a value that is already an address must not be able to fail with a
        // name-resolution error, and must not depend on this machine having a resolver to reach.
        var answer = await _resolver.ResolveAsync("93.184.216.34");

        answer.Should().ContainSingle().Which.Should().Be(IPAddress.Parse("93.184.216.34"));
    }

    [Fact]
    public async Task ResolveAsync_ALiteralIPv6Address_IsHandedBackUnresolved() =>
        (await _resolver.ResolveAsync("2606:4700:4700::1111"))
            .Should().ContainSingle().Which.Should().Be(IPAddress.Parse("2606:4700:4700::1111"));

    [Fact]
    public async Task ResolveAsync_ANameThatCannotResolve_AnswersWithNothing()
    {
        // .invalid is reserved by RFC 2606 precisely so that it never resolves, which makes "the name
        // does not resolve" a case this suite can state without depending on a network.
        var answer = await _resolver.ResolveAsync("no-such-host.invalid");

        answer.Should().BeEmpty();
    }
}
