using Bsft.Caching.Data.DependencyInjection;
using Bsft.Caching.Data.Tests.Fakes;
using Bsft.Caching.DependencyInjection;
using Bsft.Caching.Keys;
using Bsft.Data.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Caching.Data.Tests.DependencyInjection;

public class CachingDataServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftCachingData_WhenServicesIsNull_Throws()
    {
        var act = () => CachingDataServiceCollectionExtensions.AddBsftCachingData(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftCachingData_WhenCachingWasRegisteredFirst_ReplacesTheGlobalScopeProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftCaching();
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext("acme"));

        services.AddBsftCachingData();

        Resolve(services).Should().BeOfType<TenantCacheKeyScopeProvider>();
    }

    [Fact]
    public void AddBsftCachingData_WhenCachingIsRegisteredAfterwards_KeepsTheTenantScopeProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext("acme"));

        services.AddBsftCachingData();
        services.AddBsftCaching();

        Resolve(services).Should().BeOfType<TenantCacheKeyScopeProvider>();
    }

    [Fact]
    public void AddBsftCachingData_WhenTheHostRegisteredItsOwnProvider_LeavesItInPlace()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext("acme"));
        services.AddScoped<ICacheKeyScopeProvider, TenantAndUserScopeProvider>();
        services.AddBsftCaching();

        services.AddBsftCachingData();

        Resolve(services).Should().BeOfType<TenantAndUserScopeProvider>();
    }

    [Fact]
    public void AddBsftCachingData_WhenCalledTwice_RegistersOneScopeProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftCaching();

        services.AddBsftCachingData();
        services.AddBsftCachingData();

        services.Count(descriptor => descriptor.ServiceType == typeof(ICacheKeyScopeProvider))
            .Should().Be(1);
    }

    [Fact]
    public void AddBsftCachingData_WhenCalled_ReturnsTheSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        services.AddBsftCachingData().Should().BeSameAs(services);
    }

    /// <summary>Resolves the scope provider the way a request would, from its own scope.</summary>
    private static ICacheKeyScopeProvider Resolve(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        return scope.ServiceProvider.GetRequiredService<ICacheKeyScopeProvider>();
    }

    /// <summary>A host's own provider, folding more than the tenant into the scope.</summary>
    private sealed class TenantAndUserScopeProvider : ICacheKeyScopeProvider
    {
        public CacheKeyScope GetScope() => CacheKeyScope.For("tenant:acme|user:1");
    }
}
