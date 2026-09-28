using Bsft.Caching.Data.Tests.Fakes;
using Bsft.Caching.Keys;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Caching.Data.Tests;

public class TenantCacheKeyScopeProviderTests
{
    [Fact]
    public void TenantCacheKeyScopeProvider_WhenTenantContextIsNull_Throws()
    {
        var act = () => new TenantCacheKeyScopeProvider(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tenantContext");
    }

    [Fact]
    public void TenantCacheKeyScopeProvider_WhenTenantIsResolved_ScopesOnTheTenantId()
    {
        var provider = new TenantCacheKeyScopeProvider(new FakeTenantContext("acme"));

        provider.GetScope().Should().Be(CacheKeyScope.For("tenant:acme"));
    }

    [Fact]
    public void TenantCacheKeyScopeProvider_WhenTenantIsNotResolved_ReportsTheRequestUncacheable()
    {
        var provider = new TenantCacheKeyScopeProvider(new FakeTenantContext(tenantId: null));

        provider.GetScope().Should().Be(CacheKeyScope.NotCacheable);
    }

    [Fact]
    public void TenantCacheKeyScopeProvider_WhenResolvedWithoutATenantId_ReportsTheRequestUncacheable()
    {
        var provider = new TenantCacheKeyScopeProvider(new FakeTenantContext(tenantId: null, isResolved: true));

        provider.GetScope().Should().Be(CacheKeyScope.NotCacheable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TenantCacheKeyScopeProvider_WhenResolvedToABlankTenantId_ReportsTheRequestUncacheable(
        string tenantId)
    {
        // "tenant:" is a scope like any other as far as the key builder is concerned, so every request
        // whose claim carried an empty value would read and write that one bucket.
        var provider = new TenantCacheKeyScopeProvider(new FakeTenantContext(tenantId, isResolved: true));

        provider.GetScope().Should().Be(CacheKeyScope.NotCacheable);
    }

    [Fact]
    public void TenantCacheKeyScopeProvider_WhenTheTenantIsResolved_MemoizesTheComputedScope()
    {
        var context = new FakeTenantContext("acme");
        var provider = new TenantCacheKeyScopeProvider(context);

        var first = provider.GetScope();
        var second = provider.GetScope();

        first.Should().Be(second);

        // Composed once and reused: the second call must not have asked the context again.
        context.Reads.Should().Be(1);
        ReferenceEquals(first.Discriminator, second.Discriminator).Should().BeTrue();
    }

    [Fact]
    public void TenantCacheKeyScopeProvider_WhenTheTenantArrivesLaterInTheScope_StartsCachingFromThen()
    {
        var context = new FakeTenantContext(tenantId: null);
        var provider = new TenantCacheKeyScopeProvider(context);

        provider.GetScope().Should().Be(CacheKeyScope.NotCacheable);

        context.Resolve("acme");

        provider.GetScope().Should().Be(CacheKeyScope.For("tenant:acme"));
        provider.GetScope().Should().Be(CacheKeyScope.For("tenant:acme"));
    }

    [Fact]
    public void TenantCacheKeyScopeProvider_WhenTheTenantIsNotResolved_ProducesNoCacheKey()
    {
        var builder = CreateBuilder(new FakeTenantContext(tenantId: null));

        builder.Build(new TenantScopedQuery("customers")).Should().BeNull();
    }

    [Fact]
    public void TenantCacheKeyScopeProvider_WhenTwoTenantsRunTheSameQuery_ProducesDifferentCacheKeys()
    {
        var request = new TenantScopedQuery("customers");

        var first = BuildKeyFor("acme", request);
        var second = BuildKeyFor("globex", request);

        first.Should().NotBe(second);
    }

    [Fact]
    public void TenantCacheKeyScopeProvider_WhenTheSameTenantRunsTheSameQuery_ProducesOneCacheKey()
    {
        var request = new TenantScopedQuery("customers");

        BuildKeyFor("acme", request).Should().Be(BuildKeyFor("acme", request));
    }

    /// <summary>Builds the effective cache key the caching core would use for one tenant.</summary>
    private static string? BuildKeyFor(string tenantId, ICacheable request) =>
        CreateBuilder(new FakeTenantContext(tenantId)).Build(request);

    /// <summary>Creates the caching core's key builder on top of the tenant scope.</summary>
    private static CacheKeyBuilder CreateBuilder(FakeTenantContext context) =>
        new(Options.Create(new BsftCachingOptions()), new TenantCacheKeyScopeProvider(context));

    /// <summary>A cacheable request whose result differs per tenant.</summary>
    private sealed record TenantScopedQuery(string CacheKey) : ICacheable
    {
        public TimeSpan? TimeToLive => null;

        public IReadOnlyCollection<string> CacheTags => [];
    }
}
