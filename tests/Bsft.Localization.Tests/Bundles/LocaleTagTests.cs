using Bsft.Localization.Bundles;
using FluentAssertions;
using Xunit;

namespace Bsft.Localization.Tests.Bundles;

/// <summary>
/// What the bundle route accepts as a locale, and why it accepts so little.
/// </summary>
public class LocaleTagTests
{
    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("de-CH")]
    [InlineData("DE-ch")]
    [InlineData("zh-Hans")]
    [InlineData("pt-BR")]
    public void ALanguageTagTheRuntimeKnows_IsAcceptable(string locale)
    {
        LocaleTag.IsAcceptable(locale).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NothingAtAll_IsNotALocale(string? locale)
    {
        LocaleTag.IsAcceptable(locale).Should().BeFalse();
    }

    [Theory]
    [InlineData("de_CH")]
    [InlineData("de/CH")]
    [InlineData("../../etc")]
    [InlineData("de CH")]
    [InlineData("de-")]
    [InlineData("-de")]
    [InlineData("de--CH")]
    public void AStringThatIsNotShapedLikeATag_IsRefused(string locale)
    {
        LocaleTag.IsAcceptable(locale).Should().BeFalse();
    }

    [Fact]
    public void ASubtagLongerThanTheGrammarAllows_IsRefused()
    {
        LocaleTag.IsAcceptable("de-" + new string('a', 9)).Should().BeFalse();
    }

    [Fact]
    public void ATagLongerThanAnyInUse_IsRefusedBeforeItsSubtagsAreWalked()
    {
        // The length bound is what keeps the work per request constant, whatever a caller sends.
        LocaleTag.IsAcceptable(string.Join('-', Enumerable.Repeat("ab", 20))).Should().BeFalse();
    }

    [Theory]
    [InlineData("qq")]
    [InlineData("zz-ZZ")]
    [InlineData("notalang")]
    public void AWellShapedTagTheRuntimeDoesNotCarry_IsRefused(string locale)
    {
        // The shape bounds the length of a locale, this bounds how many there can be: without it a
        // caller invents tags and every one of them leaves a cached bundle behind.
        LocaleTag.IsAcceptable(locale).Should().BeFalse();
    }
}
