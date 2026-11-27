using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests;

/// <summary>The report a host reads before it canonicalizes stored addresses.</summary>
public sealed class EmailAddressCollisionsTests
{
    [Fact]
    public void Find_ForAddressesThatBecomeOne_ReportsTheGroupAndMergesNothing()
    {
        var report = ((IEnumerable<string?>)["ada@M\u00FCller.de", "ADA@xn--mller-kva.de", "bob@example.com", "ada@xn--mller-kva.de"]).FindCollisions();

        report.Collisions.Should().ContainSingle();
        report.Collisions[0].Key.Should().Be("ADA@XN--MLLER-KVA.DE");
        report.Collisions[0].Addresses.Should().Equal("ADA@xn--mller-kva.de", "ada@M\u00FCller.de", "ada@xn--mller-kva.de");
        report.IsClean.Should().BeFalse();
    }

    [Fact]
    public void Find_ForAddressesNeedingARewrite_ReportsEachWithItsCanonicalForm()
    {
        var report = ((IEnumerable<string?>)["ada@M\u00FCller.de", "bob@example.com", "carol@Example.COM"]).FindCollisions();

        report.Collisions.Should().BeEmpty();
        report.Rewrites.Should().Equal(
            new EmailAddressRewrite("ada@M\u00FCller.de", "ada@xn--mller-kva.de"),
            new EmailAddressRewrite("carol@Example.COM", "carol@example.com"));
        report.IsClean.Should().BeTrue();
    }

    [Fact]
    public void Find_ForAddressesThatAreNotCanonical_ReportsThemAsRejected()
    {
        var report = ((IEnumerable<string?>)["\u7528\u6237@example.com", "plain", null, "  ", "ok@example.com"]).FindCollisions();

        report.Rejected.Should().Equal("plain", "\u7528\u6237@example.com");
        report.IsClean.Should().BeFalse();
    }

    [Fact]
    public void Find_ForARepeatedSpelling_CountsItOnce()
    {
        var report = ((IEnumerable<string?>)["a@example.com", "a@example.com"]).FindCollisions();

        report.Collisions.Should().BeEmpty();
    }

    [Fact]
    public void Find_ForNull_Throws()
    {
        var act = () => EmailAddressCollisions.FindCollisions(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
