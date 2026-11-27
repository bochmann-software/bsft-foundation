using FluentAssertions;
using Xunit;

namespace Bsft.Localization.Tests;

/// <summary>
/// The cache key compares its three parts as parts, so no spelling of one part can read another
/// entry's value.
/// </summary>
public class TranslationCacheKeyTests
{
    [Fact]
    public void Equals_SameParts_AreEqualWithTheSameHash()
    {
        var first = new TranslationCacheKey("t1", "en", "greeting");
        var second = new TranslationCacheKey("t1", "en", "greeting");

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Equals_LocaleAndKeyDifferingOnlyInCase_AreDifferent()
    {
        var spelledLower = new TranslationCacheKey("t1", "en-us", "app.title");
        var spelledUpper = new TranslationCacheKey("t1", "EN-US", "APP.TITLE");
        var localeOnly = new TranslationCacheKey("t1", "EN-US", "app.title");
        var keyOnly = new TranslationCacheKey("t1", "en-us", "APP.TITLE");

        spelledLower.Should().NotBe(spelledUpper);
        spelledLower.Should().NotBe(localeOnly);
        spelledLower.Should().NotBe(keyOnly);
    }

    [Fact]
    public void Equals_AbsentTenantAgainstANamedTenant_AreDifferent()
    {
        var absent = new TranslationCacheKey(null, "en", "greeting");
        var named = new TranslationCacheKey("t1", "en", "greeting");

        absent.Should().NotBe(named);
    }

    [Fact]
    public void Equals_AbsentTenantAgainstEmptyTenant_AreDifferent()
    {
        var absent = new TranslationCacheKey(null, "en", "greeting");
        var empty = new TranslationCacheKey(string.Empty, "en", "greeting");

        absent.Should().NotBe(empty);
    }

    [Fact]
    public void Equals_AbsentTenantAgainstTheWordNull_AreDifferent()
    {
        var absent = new TranslationCacheKey(null, "en", "greeting");
        var literal = new TranslationCacheKey("null", "en", "greeting");

        absent.Should().NotBe(literal);
    }

    [Fact]
    public void Equals_PartsThatJoinToTheSameText_AreDifferent()
    {
        var pipeInTenant = new TranslationCacheKey("a|b", "c", "d");
        var pipeInLocale = new TranslationCacheKey("a", "b|c", "d");
        var pipeInKey = new TranslationCacheKey("a", "b", "c|d");

        pipeInTenant.Should().NotBe(pipeInKey);
        pipeInLocale.Should().NotBe(pipeInKey);
        pipeInTenant.Should().NotBe(pipeInLocale);
    }

    [Fact]
    public void Equals_TenantsDifferingOnlyInCase_AreDifferent()
    {
        var lower = new TranslationCacheKey("t1", "en", "greeting");
        var upper = new TranslationCacheKey("T1", "en", "greeting");

        lower.Should().NotBe(upper);
    }

    [Fact]
    public void EqualityOperator_KeysNamingTheSameEntry_IsTrueAndInequalityFalse()
    {
        var first = new TranslationCacheKey("t1", "en", "app.title");
        var second = new TranslationCacheKey("t1", "en", "app.title");

        (first == second).Should().BeTrue();
        (first != second).Should().BeFalse();
    }

    [Fact]
    public void EqualityOperator_KeysDifferingOnlyInCase_IsFalseAndInequalityTrue()
    {
        var first = new TranslationCacheKey("t1", "en", "app.title");
        var second = new TranslationCacheKey("t1", "EN", "APP.TITLE");

        (first == second).Should().BeFalse();
        (first != second).Should().BeTrue();
    }

    [Fact]
    public void EqualityOperator_KeysOfDifferentTenants_IsFalseAndInequalityTrue()
    {
        var first = new TranslationCacheKey("t1", "en", "app.title");
        var second = new TranslationCacheKey("t2", "en", "app.title");

        (first == second).Should().BeFalse();
        (first != second).Should().BeTrue();
    }
}
