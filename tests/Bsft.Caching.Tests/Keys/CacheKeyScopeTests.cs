using Bsft.Caching.Keys;
using FluentAssertions;
using Xunit;

namespace Bsft.Caching.Tests.Keys;

public class CacheKeyScopeTests
{
    [Fact]
    public void CacheKeyScope_WhenGlobal_IsCacheableWithoutADiscriminator()
    {
        var scope = CacheKeyScope.Global;

        scope.IsCacheable.Should().BeTrue();
        scope.Discriminator.Should().BeNull();
    }

    [Fact]
    public void CacheKeyScope_WhenNotCacheable_CarriesNeitherCacheabilityNorADiscriminator()
    {
        var scope = CacheKeyScope.NotCacheable;

        scope.IsCacheable.Should().BeFalse();
        scope.Discriminator.Should().BeNull();
    }

    [Fact]
    public void CacheKeyScope_WhenCreatedForADiscriminator_IsCacheableAndCarriesIt()
    {
        var scope = CacheKeyScope.For("tenant:acme");

        scope.IsCacheable.Should().BeTrue();
        scope.Discriminator.Should().Be("tenant:acme");
    }

    [Fact]
    public void CacheKeyScope_WhenCreatedForANullDiscriminator_Throws()
    {
        var act = () => CacheKeyScope.For(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("discriminator");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CacheKeyScope_WhenCreatedForABlankDiscriminator_Throws(string discriminator)
    {
        // It would qualify the key with nothing, which is what the global scope does, so every caller
        // whose discriminator failed to resolve would read and write one another's entries.
        var act = () => CacheKeyScope.For(discriminator);

        act.Should().Throw<ArgumentException>().WithParameterName("discriminator");
    }

    [Fact]
    public void CacheKeyScope_WhenDefaultConstructed_IsNotCacheable()
    {
        default(CacheKeyScope).Should().Be(CacheKeyScope.NotCacheable);
    }

    [Fact]
    public void CacheKeyScope_WhenDiscriminatorsDiffer_TheScopesDiffer()
    {
        CacheKeyScope.For("tenant:acme").Should().NotBe(CacheKeyScope.For("tenant:globex"));
        CacheKeyScope.Global.Should().NotBe(CacheKeyScope.NotCacheable);
    }

    [Fact]
    public void CacheKeyScope_WhenComparedByOperator_EqualsTheScopeCarryingTheSameDiscriminator()
    {
        (CacheKeyScope.For("tenant:acme") == CacheKeyScope.For("tenant:acme")).Should().BeTrue();
        (CacheKeyScope.For("tenant:acme") != CacheKeyScope.For("tenant:globex")).Should().BeTrue();
        (CacheKeyScope.Global == CacheKeyScope.NotCacheable).Should().BeFalse();
    }

    [Fact]
    public void CacheKeyScope_WhenComparedToAnotherType_IsNotEqual()
    {
        CacheKeyScope.Global.Equals("global").Should().BeFalse();
    }

    [Fact]
    public void CacheKeyScope_WhenEqual_SharesTheHashCode()
    {
        CacheKeyScope.For("tenant:acme").GetHashCode()
            .Should().Be(CacheKeyScope.For("tenant:acme").GetHashCode());

        CacheKeyScope.Global.GetHashCode().Should().Be(CacheKeyScope.Global.GetHashCode());
    }

    [Fact]
    public void CacheKeyScope_WhenFormatted_NamesTheState()
    {
        CacheKeyScope.For("tenant:acme").ToString().Should().Be("tenant:acme");
        CacheKeyScope.Global.ToString().Should().Be("<global>");
        CacheKeyScope.NotCacheable.ToString().Should().Be("<not cacheable>");
    }
}
