using System.Net;
using FluentAssertions;
using Xunit;

namespace Bsft.Http.Egress.Tests;

public class OutboundAddressRulesTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void IsPublic_APublicAddress_IsAllowed(string address) =>
        IPAddress.Parse(address).IsPublicAddress().Should().BeTrue();

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("127.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("192.168.1.1")]
    [InlineData("192.88.99.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.9")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    public void IsPublic_AnAddressThatIsNotThePublicInternet_IsRefused(string address) =>
        IPAddress.Parse(address).IsPublicAddress().Should().BeFalse();

    [Theory]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    public void IsPublic_AnIPv6AddressThatIsNotThePublicInternet_IsRefused(string address) =>
        IPAddress.Parse(address).IsPublicAddress().Should().BeFalse();

    [Fact]
    public void IsPublic_AnIPv4LoopbackWrittenAsIPv6_IsStillLoopback() =>
        IPAddress.Parse("::ffff:127.0.0.1").IsPublicAddress().Should().BeFalse();

    /// <summary>
    /// The deprecated IPv4-compatible form is the mapped form without the <c>ffff</c>, and .NET does
    /// not report it as mapped. Judging it as an unlisted IPv6 address would be a second spelling of
    /// the smuggling the mapped form is already stopped from doing.
    /// </summary>
    [Theory]
    [InlineData("::169.254.169.254")]
    [InlineData("::10.1.2.3")]
    public void IsPublic_APrivateAddressInTheDeprecatedCompatibleForm_IsRefused(string address) =>
        IPAddress.Parse(address).IsPublicAddress().Should().BeFalse();

    /// <summary>The same form carrying a public address is public, and is not refused wholesale.</summary>
    [Fact]
    public void IsPublic_APublicAddressInTheDeprecatedCompatibleForm_IsAllowed() =>
        IPAddress.Parse("::8.8.8.8").IsPublicAddress().Should().BeTrue();

    [Fact]
    public void IsPublic_AMetadataAddressInsideASixToFourPrefix_IsRefused() =>
        IPAddress.Parse("2002:a9fe:a9fe::1").IsPublicAddress().Should().BeFalse();

    [Fact]
    public void IsPublic_APublicAddressInsideASixToFourPrefix_IsAllowed() =>
        IPAddress.Parse("2002:0808:0808::1").IsPublicAddress().Should().BeTrue();

    [Fact]
    public void IsPublic_APrivateAddressInsideTheNat64Prefix_IsRefused() =>
        IPAddress.Parse("64:ff9b::a00:1").IsPublicAddress().Should().BeFalse();

    [Fact]
    public void IsPublic_APublicAddressInsideTheNat64Prefix_IsAllowed() =>
        IPAddress.Parse("64:ff9b::808:808").IsPublicAddress().Should().BeTrue();

    [Fact]
    public void IsPublic_NullAddress_Throws()
    {
        var act = () => ((IPAddress)null!).IsPublicAddress();

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("intranet")]
    [InlineData("db")]
    [InlineData("metadata")]
    public void IsSingleLabelHost_ANameWithNoDot_IsRefused(string host) =>
        host.IsSingleLabelHost().Should().BeTrue();

    [Theory]
    [InlineData("hooks.example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSingleLabelHost_AnythingElse_IsNotSingleLabel(string? host) =>
        host.IsSingleLabelHost().Should().BeFalse();

    [Fact]
    public void IsSingleLabelHost_AnIPv6Literal_IsNotSingleLabel() =>
        "::1".IsSingleLabelHost().Should().BeFalse();

    /// <summary>
    /// The absolute form of a one-label name is the same one label. A check that read the trailing
    /// dot as a second label would let <c>metadata.</c> reach exactly what <c>metadata</c> may not.
    /// </summary>
    [Theory]
    [InlineData("metadata.")]
    [InlineData("intranet..")]
    public void IsSingleLabelHost_ANameWithATrailingDot_IsStillOneLabel(string host) =>
        host.IsSingleLabelHost().Should().BeTrue();

    /// <summary>A dotted name stays dotted when it is written absolutely.</summary>
    [Fact]
    public void IsSingleLabelHost_ADottedNameWithATrailingDot_IsNotSingleLabel() =>
        "hooks.example.com.".IsSingleLabelHost().Should().BeFalse();

    /// <summary>A host of nothing but dots names no label at all, and is refused with the rest.</summary>
    [Fact]
    public void IsSingleLabelHost_NothingButDots_IsRefused() =>
        ".".IsSingleLabelHost().Should().BeTrue();
}
