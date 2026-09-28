using FluentAssertions;
using Xunit;

namespace Bsft.Http.Egress.Tests;

public class OutboundAddressDecisionTests
{
    [Fact]
    public void Allowed_CarriesNoReason()
    {
        OutboundAddressDecision.Allowed.IsAllowed.Should().BeTrue();
        OutboundAddressDecision.Allowed.Reason.Should().BeEmpty();
    }

    [Fact]
    public void Denied_CarriesTheReason()
    {
        var decision = OutboundAddressDecision.Denied("the endpoint is internal");

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Be("the endpoint is internal");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Denied_WithoutAReason_Throws(string reason)
    {
        // A refusal is reported onwards, so one that says nothing is a refusal nobody can act on.
        var act = () => OutboundAddressDecision.Denied(reason);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Denied_WithoutAReasonAtAll_Throws()
    {
        var act = () => OutboundAddressDecision.Denied(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
