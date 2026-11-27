using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests;

/// <summary>
/// The canonical domain. The vectors are fixed values, not answers of <c>IdnMapping</c>: the result must be the same on
/// Linux (ICU) and Windows, so the CI runs this class on both.
/// </summary>
public sealed class InternetDomainTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("EXAMPLE.Com", "example.com")]
    [InlineData("  example.com\t", "example.com")]
    [InlineData("a-b.example.co.uk", "a-b.example.co.uk")]
    [InlineData("a.b1", "a.b1")]
    [InlineData("m\u00FCller.de", "xn--mller-kva.de")]
    [InlineData("M\u00DCLLER.DE", "xn--mller-kva.de")]
    [InlineData("mu\u0308ller.de", "xn--mller-kva.de")]
    [InlineData("xn--mller-kva.de", "xn--mller-kva.de")]
    [InlineData("XN--MLLER-KVA.DE", "xn--mller-kva.de")]
    [InlineData("\u65E5\u672C\u8A9E.jp", "xn--wgv71a119e.jp")]
    [InlineData("\u043F\u0440\u0438\u043C\u0435\u0440.xn--p1ai", "xn--e1afmkfd.xn--p1ai")]
    [InlineData("\u043F\u0440\u0438\u043C\u0435\u0440.\u0440\u0444", "xn--e1afmkfd.xn--p1ai")]
    public void TryCanonicalize_ForAValidName_ReturnsTheAsciiForm(string domain, string expected)
    {
        domain.TryCanonicalizeDomain(out var canonical).Should().BeTrue();

        canonical.Should().Be(expected);
    }

    [Theory]
    [InlineData("\uFF45\uFF58\uFF41\uFF4D\uFF50\uFF4C\uFF45\uFF0Ecom")]
    [InlineData("example\u3002com")]
    [InlineData("example\uFF61com")]
    [InlineData("exa\u00ADmple.com")]
    [InlineData("exa\u200Bmple.com")]
    public void TryCanonicalize_ForFullWidthDotsAndCharacters_AndIgnoredCodePoints_ReturnsThePlainName(string domain)
    {
        domain.TryCanonicalizeDomain(out var canonical).Should().BeTrue();

        canonical.Should().Be("example.com");
    }

    [Theory]
    [InlineData("fa\u00DF.de", "xn--fa-hia.de")]
    [InlineData("FA\u1E9E.DE", "xn--fa-hia.de")]
    [InlineData("fass.de", "fass.de")]
    [InlineData("\u03B2\u03CC\u03BB\u03BF\u03C2.com", "xn--nxasmm1c.com")]
    public void TryCanonicalize_ForADeviationCharacter_KeepsItNonTransitionally(string domain, string expected)
    {
        domain.TryCanonicalizeDomain(out var canonical).Should().BeTrue();

        canonical.Should().Be(expected);
    }

    [Theory]
    [InlineData("ab\u0915\u094D\u200C\u0937.com", "xn--ab-nnf0lney10n.com")]
    [InlineData("\u0915\u094D\u200D\u0937.com", "xn--11b2ezcw70k.com")]
    [InlineData("\u0628\u200C\u0628.com", "xn--ngba799q.com")]
    public void TryCanonicalize_ForAJoinerInItsContext_KeepsTheJoiner(string domain, string expected)
    {
        domain.TryCanonicalizeDomain(out var canonical).Should().BeTrue();

        canonical.Should().Be(expected);
    }

    [Fact]
    public void TryCanonicalizeDomain_ForAJoinerBetweenJoiningLettersWithTransparentMarksAround_KeepsTheJoiner()
    {
        const string label = "\u0628\u064E\u200C\u064F\u0628";

        (label + ".com").TryCanonicalizeDomain(out var canonical).Should().BeTrue();

        canonical!.ToUnicodeDomain().Should().Be(label + ".com");
    }

    [Theory]
    [InlineData("a\u200Db.com")]
    [InlineData("a\u200Cb.com")]
    [InlineData("\u200Da.com")]
    [InlineData("\u200C\u0628.com")]
    [InlineData("\u0628\u200C.com")]
    [InlineData("a\u0915\u094D\u200Cb.com\u200D")]
    public void TryCanonicalize_ForAJoinerOutOfContext_Refuses(string domain)
        => domain.TryCanonicalizeDomain(out _).Should().BeFalse();

    [Theory]
    [InlineData("\u05D0\u05D1\u05D2.com", "xn--4dbcd.com")]
    [InlineData("\u05D0\u05D1\u05D2\u05B0.com", "xn--7cb7dde.com")]
    [InlineData("\u05D0\u05D11.com", "xn--1-zhcd.com")]
    [InlineData("\u05D0\u05D1\u0663.com", "xn--4dbc25b.com")]
    public void TryCanonicalize_ForARightToLeftLabelThatMeetsTheBidiRule_Accepts(string domain, string expected)
    {
        domain.TryCanonicalizeDomain(out var canonical).Should().BeTrue();

        canonical.Should().Be(expected);
    }

    [Theory]
    [InlineData("\u05D0\u05D1\u05D2a.com")]
    [InlineData("1\u05D0\u05D1\u05D2.com")]
    [InlineData("\u05D0\u05D11\u0663.com")]
    [InlineData("\u05D0\u05D1\u00B7.com")]
    [InlineData("1com.\u05D0\u05D1\u05D2")]
    [InlineData("\u00B7a.\u05D0\u05D1\u05D2.com")]
    [InlineData("a\u00B7.\u05D0\u05D1\u05D2.com")]
    public void TryCanonicalize_ForABidiNameThatBreaksTheBidiRule_Refuses(string domain)
        => domain.TryCanonicalizeDomain(out _).Should().BeFalse();

    [Fact]
    public void TryCanonicalize_ForALeftToRightLabelBesideARightToLeftOne_AcceptsALabelEndingInADigit()
    {
        "a1.\u05D0\u05D1\u05D2".TryCanonicalizeDomain(out var canonical).Should().BeTrue();

        canonical.Should().Be("a1.xn--4dbcd");
    }

    [Fact]
    public void TryCanonicalize_ForANumericNameWithoutRightToLeftText_AcceptsALeadingDigit()
    {
        "1com.example".TryCanonicalizeDomain(out var canonical).Should().BeTrue();

        canonical.Should().Be("1com.example");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example")]
    [InlineData("example.com.")]
    [InlineData(".example.com")]
    [InlineData("a..com")]
    [InlineData("-a.com")]
    [InlineData("a-.com")]
    [InlineData("ab--cd.com")]
    [InlineData("exa mple.com")]
    [InlineData("exa_mple.com")]
    [InlineData("exa@mple.com")]
    [InlineData("example.123")]
    [InlineData("1.2.3.4")]
    [InlineData("exa\u0378mple.com")]
    [InlineData("exa\uFFFDmple.com")]
    [InlineData("exa\uD800mple.com")]
    [InlineData("\u0301a.com")]
    public void TryCanonicalize_ForANameThatIsNotRegistrable_Refuses(string? domain)
    {
        domain.TryCanonicalizeDomain(out var canonical).Should().BeFalse();

        canonical.Should().BeNull();
    }

    [Theory]
    [InlineData("xn--.com")]
    [InlineData("xn--a.com")]
    [InlineData("xn--mller-kv.de")]
    [InlineData("xn--mller-kva!.de")]
    [InlineData("xn--m\u00FCller-kva.de")]
    [InlineData("xn---kva.de")]
    [InlineData("xn--mller\u0080-kva.de")]
    [InlineData("xn--muller-xyd.de")]
    [InlineData("xn--zzzzzzzzzzzzzzzzzzzz.de")]
    [InlineData("xn--9999999999999.de")]
    [InlineData("xn--en32g.com")]
    [InlineData("xn--ib9b.com")]
    [InlineData("xn--mller-.de")]
    [InlineData("xn--7ba.de")]
    [InlineData("xn--a-.de")]
    public void TryCanonicalize_ForAnAcePrefixedLabelThatIsNotValidPunycode_Refuses(string domain)
        => domain.TryCanonicalizeDomain(out _).Should().BeFalse();

    [Fact]
    public void TryCanonicalize_AtTheLengthBounds_AcceptsTheLongestAndRefusesOneMore()
    {
        var label = new string('a', 63);

        (label + ".com").TryCanonicalizeDomain(out _).Should().BeTrue();
        (label + "a.com").TryCanonicalizeDomain(out _).Should().BeFalse("a label has at most 63");
        (string.Join('.', label, label, label, new string('a', 59) + ".c")).TryCanonicalizeDomain(out _).Should().BeTrue();
        (string.Join('.', label, label, label, new string('a', 60) + ".c")).TryCanonicalizeDomain(out _).Should().BeFalse("the name has at most 253");
        (CjkLabel(0, 40) + ".de").TryCanonicalizeDomain(out _).Should().BeFalse("the A-label is longer than 63");
        (string.Join('.', Enumerable.Range(0, 7).Select(index => CjkLabel(index * 20, 17))) + ".de").TryCanonicalizeDomain(out _).Should().BeFalse("the A-labels together exceed 253");
    }

    private static string CjkLabel(int offset, int count)
        => string.Concat(Enumerable.Range(0, count).Select(index => char.ConvertFromUtf32(0x4E00 + (((offset + index) * 211) % 0x4000))));

    [Fact]
    public void TryCanonicalize_ForHyphensAtPositionsThreeAndFourAfterASupplementaryCharacter_CountsCodePoints()
    {
        "\U00020000a--b.com".TryCanonicalizeDomain(out _).Should().BeFalse("the hyphens are at the third and fourth code point");
        "\U00020000--b.com".TryCanonicalizeDomain(out var canonical).Should().BeTrue("the hyphens are at the second and third code point");

        canonical!.ToUnicodeDomain().Should().Be("\U00020000--b.com");
    }

    [Theory]
    [InlineData("mu\u0308ller.de", "m\u00FCller.de")]
    [InlineData("x\u1ACF\u0323.com", "x\u0323\u1ACF.com")]
    [InlineData("\u1100\u1161\u11A8.kr", "\uAC01.kr")]
    public void TryCanonicalize_ForCanonicallyEquivalentNames_ReturnsOneName(string first, string second)
    {
        first.TryCanonicalizeDomain(out var firstCanonical).Should().BeTrue();
        second.TryCanonicalizeDomain(out var secondCanonical).Should().BeTrue();

        firstCanonical.Should().Be(secondCanonical);
    }

    [Fact]
    public void TryCanonicalize_ForADecomposedALabel_IsRefused()
        => "xn--muller-xyd.de".TryCanonicalizeDomain(out _).Should().BeFalse("the label decodes to a name that is not in form C");

    [Fact]
    public void MaximumLength_Is253()
        => InternetDomain.MaximumLength.Should().Be(253);

    [Theory]
    [InlineData("xn--mller-kva.de", "m\u00FCller.de")]
    [InlineData("example.com", "example.com")]
    [InlineData("xn--.de", ".de")]
    public void ToUnicode_DecodesTheALabels(string canonical, string expected)
        => canonical.ToUnicodeDomain().Should().Be(expected);

    [Fact]
    public void ToUnicode_ForNull_Throws()
    {
        var act = () => InternetDomain.ToUnicodeDomain(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
