using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests;

/// <summary>The canonical address: an ASCII local part as entered, the domain as its canonical A-label form.</summary>
public sealed class EmailAddressTests
{
    [Theory]
    [InlineData("someone@example.com")]
    [InlineData("  someone@example.com\t")]
    [InlineData("first.last+tag@sub.example.co.uk")]
    [InlineData("a@b.cc")]
    [InlineData("o'brien@example.com")]
    [InlineData("a_b-c@x-y.example.com")]
    [InlineData("a@example.c0m")]
    public void TryCanonicalize_ForAnAsciiAddress_ReturnsTheTrimmedValue(string address)
    {
        address.TryCanonicalize(out var canonical).Should().BeTrue();

        canonical.Should().Be(address.Trim());
    }

    [Theory]
    [InlineData("UPPER@Example.COM", "UPPER@example.com")]
    [InlineData("Ada@M\u00FCller.DE", "Ada@xn--mller-kva.de")]
    [InlineData("ada@xn--mller-kva.de", "ada@xn--mller-kva.de")]
    [InlineData("ada@m\u00FCller\u3002de", "ada@xn--mller-kva.de")]
    [InlineData("ada@corp.com\u00AD", "ada@corp.com")]
    public void TryCanonicalize_KeepsTheLocalPartAndConvertsTheDomain(string address, string expected)
    {
        address.TryCanonicalize(out var canonical).Should().BeTrue();

        canonical.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nobody")]
    [InlineData("@example.com")]
    [InlineData("someone@")]
    [InlineData("someone@localhost")]
    [InlineData("someone@example")]
    [InlineData("someone@example.123")]
    [InlineData("someone@1.2.3.4")]
    [InlineData("a@b@example.com")]
    [InlineData("some one@example.com")]
    [InlineData("someone@exa mple.com")]
    [InlineData("\"quoted\"@example.com")]
    [InlineData("\"victim@corp.com\" <attacker@evil.example>")]
    [InlineData("Victim <attacker@evil.example>")]
    [InlineData("attacker@evil.example (victim@corp.com)")]
    [InlineData("someone@[127.0.0.1]")]
    [InlineData("a@example.com,b@example.com")]
    [InlineData("a@example.com;b@example.com")]
    [InlineData("a:b@example.com")]
    [InlineData("a\\b@example.com")]
    [InlineData("someone@example.com\r\nBcc: x@evil.example")]
    public void TryCanonicalize_ForAnAddressThatIsNotAPlainAddrSpec_Refuses(string? address)
    {
        address.TryCanonicalize(out var canonical).Should().BeFalse();

        canonical.Should().BeNull();
    }

    [Theory]
    [InlineData("victim@corp.com\uFE6Bevil.com")]
    [InlineData("victim\uFF20corp.com@evil.com")]
    [InlineData("vict\u00ADim@corp.com")]
    [InlineData("v\u00EFctim@corp.com")]
    [InlineData("\u7528\u6237@example.com")]
    [InlineData("victim@corp\u0378.com")]
    public void TryCanonicalize_ForANonAsciiLocalPart_OrADomainThatIsNotRegistrable_Refuses(string address)
        => address.TryCanonicalize(out _).Should().BeFalse();

    [Theory]
    [InlineData("victim@corp.com.")]
    [InlineData("victim.@corp.com")]
    [InlineData(".victim@corp.com")]
    [InlineData("vic..tim@corp.com")]
    [InlineData("victim@.corp.com")]
    [InlineData("victim@corp..com")]
    [InlineData("victim@-corp.com")]
    [InlineData("victim@corp-.com")]
    public void TryCanonicalize_ForMisplacedDots_AndHyphens_Refuses(string address)
        => address.TryCanonicalize(out _).Should().BeFalse();

    [Fact]
    public void TryCanonicalize_AtTheLengthBounds_AcceptsTheLongestAndRefusesOneMore()
    {
        var longest = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 57) + ".com";

        longest.Length.Should().Be(254);
        longest.TryCanonicalize(out _).Should().BeTrue();
        (longest + "m").TryCanonicalize(out _).Should().BeFalse();
        (new string('a', 65) + "@example.com").TryCanonicalize(out _).Should().BeFalse("the local part has at most 64");
        ("a@" + new string('b', 64) + ".com").TryCanonicalize(out _).Should().BeFalse("a label has at most 63");
        ("a@" + new string('b', 63) + ".com").TryCanonicalize(out _).Should().BeTrue();
    }

    [Fact]
    public void TryCanonicalize_WhenTheAsciiFormOfTheDomainOverrunsTheLimit_Refuses()
    {
        var domain = string.Join('.', Enumerable.Range(0, 5).Select(index => string.Concat(Enumerable.Range(0, 12).Select(offset => char.ConvertFromUtf32(0x4E00 + (((index * 20) + offset) * 211 % 0x4000))))));
        var address = new string('a', 64) + "@" + domain;

        domain.TryCanonicalizeDomain(out _).Should().BeTrue();

        address.Length.Should().BeLessThan(EmailAddress.MaximumLength);
        address.TryCanonicalize(out _).Should().BeFalse();
    }

    [Fact]
    public void MaximumLength_Is254()
        => EmailAddress.MaximumLength.Should().Be(254);

    [Theory]
    [InlineData("Ada@Example.com", "ADA@EXAMPLE.COM")]
    [InlineData("ada@M\u00FCller.de", "ADA@XN--MLLER-KVA.DE")]
    [InlineData("ADA@xn--mller-kva.de", "ADA@XN--MLLER-KVA.DE")]
    public void TryFold_ReturnsOneKeyForEverySpellingOfAnAddress(string address, string expected)
    {
        address.TryFold(out var folded).Should().BeTrue();

        folded.Should().Be(expected);
    }

    [Fact]
    public void TryFold_ForAnAddressThatIsNotCanonical_ReturnsNull()
    {
        "not an address".TryFold(out var folded).Should().BeFalse();

        folded.Should().BeNull();
    }
}
