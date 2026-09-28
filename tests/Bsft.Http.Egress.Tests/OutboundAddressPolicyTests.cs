using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Http.Egress.Tests;

public class OutboundAddressPolicyTests
{
    [Fact]
    public void Constructor_WithoutOptions_Throws()
    {
        var act = () => new OutboundAddressPolicy(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("https://hooks.example.com/deliver")]
    [InlineData("https://hooks.example.com:8443/deliver")]
    public void Evaluate_APublicHttpsUrl_IsAllowed(string url) =>
        Policy().Evaluate(new Uri(url)).IsAllowed.Should().BeTrue();

    [Fact]
    public void Evaluate_ARelativeUrl_IsRefused()
    {
        var decision = Policy().Evaluate(new Uri("/deliver", UriKind.Relative));

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("absolute");
    }

    [Fact]
    public void Evaluate_NoUrlAtAll_IsRefused() =>
        Policy().Evaluate(url: null).IsAllowed.Should().BeFalse();

    [Fact]
    public void Evaluate_ASchemeThatIsNotHttp_IsRefused()
    {
        var decision = Policy().Evaluate(new Uri("ftp://hooks.example.com/deliver"));

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("absolute");
    }

    [Fact]
    public void Evaluate_PlainHttp_IsRefused()
    {
        var decision = Policy().Evaluate(new Uri("http://hooks.example.com/deliver"));

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("https");
    }

    [Fact]
    public void Evaluate_PlainHttpUnderTheOverride_IsAllowed() =>
        Policy(options => options.AllowInsecureUrls = true)
            .Evaluate(new Uri("http://hooks.example.com/deliver"))
            .IsAllowed.Should().BeTrue();

    [Fact]
    public void Evaluate_ASingleLabelHost_IsRefused()
    {
        var decision = Policy().Evaluate(new Uri("https://intranet/deliver"));

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("fully qualified");
    }

    [Fact]
    public void Evaluate_TheMetadataAddressWrittenOutright_IsRefused()
    {
        // Nothing resolves this URL, so the connect-time guard is not what catches it: judging the
        // literal address here is the whole defence for this shape.
        var decision = Policy().Evaluate(new Uri("https://169.254.169.254/latest/meta-data/"));

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Evaluate_APublicAddressWrittenOutright_IsAllowed() =>
        Policy().Evaluate(new Uri("https://93.184.216.34/deliver")).IsAllowed.Should().BeTrue();

    [Fact]
    public void Evaluate_AnAllowListedSingleLabelHost_IsAllowed() =>
        Policy(options => options.AllowedHosts.Add("relay"))
            .Evaluate(new Uri("https://relay/send"))
            .IsAllowed.Should().BeTrue();

    [Fact]
    public void EvaluateHost_TheAllowListIsMatchedWithoutRegardToCase() =>
        Policy(options => options.AllowedHosts.Add("Relay.Internal"))
            .EvaluateHost("relay.internal")
            .IsAllowed.Should().BeTrue();

    [Fact]
    public void EvaluateHost_AHostNamedFurtherDownTheAllowList_IsStillFound() =>
        // The list is walked to the end rather than judged on its first entry, so the order an operator
        // happens to write the hosts in does not decide which of them are let through.
        Policy(options =>
            {
                options.AllowedHosts.Add("metrics.internal");
                options.AllowedHosts.Add("relay.internal");
            })
            .EvaluateHost("relay.internal")
            .IsAllowed.Should().BeTrue();

    [Fact]
    public void EvaluateHost_NoHostAtAll_IsAllowed() =>
        // An empty host is not this policy's refusal to make: it is not a name that resolves anywhere,
        // and the URL rules above have already refused the shapes that can produce one.
        Policy().EvaluateHost(host: null).IsAllowed.Should().BeTrue();

    [Fact]
    public void EvaluateAddress_APrivateAddress_IsRefused()
    {
        var decision = Policy().EvaluateAddress("hooks.example.com", IPAddress.Parse("10.0.0.7"));

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().NotContain("10.0.0.7");
    }

    [Fact]
    public void EvaluateAddress_APrivateAddressUnderTheOverride_IsAllowed() =>
        Policy(options => options.AllowPrivateNetworks = true)
            .EvaluateAddress("localhost", IPAddress.Loopback)
            .IsAllowed.Should().BeTrue();

    [Fact]
    public void EvaluateAddress_AnAllowListedHost_IsAllowedAtAnyAddress() =>
        Policy(options => options.AllowedHosts.Add("relay.internal"))
            .EvaluateAddress("relay.internal", IPAddress.Parse("10.0.0.7"))
            .IsAllowed.Should().BeTrue();

    [Fact]
    public void EvaluateAddress_WithoutAnAddress_Throws()
    {
        var act = () => Policy().EvaluateAddress("hooks.example.com", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static IOutboundAddressPolicy Policy(Action<BsftHttpEgressOptions>? configure = null)
    {
        var options = new BsftHttpEgressOptions();
        configure?.Invoke(options);

        return new OutboundAddressPolicy(Microsoft.Extensions.Options.Options.Create(options));
    }
}
