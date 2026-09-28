using FluentAssertions;
using Xunit;

namespace Bsft.Caching.Tests;

public class BsftCachingOptionsTests
{
    [Fact]
    public void Options_ExposeTheConfiguredValues()
    {
        var options = new BsftCachingOptions
        {
            Provider = CacheProvider.Distributed,
            DefaultTimeToLive = TimeSpan.FromMinutes(9),
            KeyPrefix = "custom",
        };

        options.Provider.Should().Be(CacheProvider.Distributed);
        options.DefaultTimeToLive.Should().Be(TimeSpan.FromMinutes(9));
        options.KeyPrefix.Should().Be("custom");
    }

    [Fact]
    public void Options_Defaults_AreMemoryAndFiveMinutes()
    {
        var options = new BsftCachingOptions();

        options.Provider.Should().Be(CacheProvider.Memory);
        options.DefaultTimeToLive.Should().Be(TimeSpan.FromMinutes(5));
        options.KeyPrefix.Should().Be("bsft");
    }

    [Fact]
    public void Cacheable_Defaults_AreNoLifetimeAndNoTags()
    {
        ICacheable query = new DefaultsQuery("k");

        query.TimeToLive.Should().BeNull();
        query.CacheTags.Should().BeEmpty();
    }

    [Fact]
    public void CacheLookupResult_CarriesHitAndValue()
    {
        var hit = new CacheLookupResult<string>(true, "value");
        var miss = new CacheLookupResult<string>(false, null);

        hit.IsHit.Should().BeTrue();
        hit.Value.Should().Be("value");
        miss.IsHit.Should().BeFalse();
        miss.Value.Should().BeNull();
    }
}
