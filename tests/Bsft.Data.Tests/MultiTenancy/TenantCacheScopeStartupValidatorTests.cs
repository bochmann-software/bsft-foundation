using Bsft.Caching;
using Bsft.Caching.Data;
using Bsft.Caching.Data.DependencyInjection;
using Bsft.Caching.DependencyInjection;
using Bsft.Caching.Keys;
using Bsft.Core;
using Bsft.Data.DependencyInjection;
using Bsft.Data.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

public class TenantCacheScopeStartupValidatorTests
{
    [Fact]
    public void TenantCacheScopeStartupValidator_WhenServicesIsNull_Throws()
    {
        var act = () => new TenantCacheScopeStartupValidator(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public async Task TenantCacheScopeStartupValidator_WhenCachingKeepsTheGlobalScope_WarnsAndPasses()
    {
        var services = new ServiceCollection();
        services.AddBsftCaching();

        var result = await new TenantCacheScopeStartupValidator(services)
            .ValidateAsync(CancellationToken.None);

        result.IsValid.Should().BeTrue("a global cache is a configuration, not a defect the host cannot ship with");
        result.Warnings.Should().ContainSingle();
        result.Warnings[0].Should()
            .Contain("ICacheKeyScopeProvider").And
            .Contain("TenantCacheKeyScopeProvider").And
            .Contain("AddBsftCachingData");
    }

    [Fact]
    public async Task TenantCacheScopeStartupValidator_WhenTheTenantScopeIsRegistered_StaysSilent()
    {
        var services = new ServiceCollection();
        services.AddBsftCaching();
        services.AddBsftCachingData();

        var result = await new TenantCacheScopeStartupValidator(services)
            .ValidateAsync(CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Message.Should().Be("The cache key scope is not the tenant-blind default.");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task TenantCacheScopeStartupValidator_WhenTheHostReplacedTheScopeProvider_StaysSilent()
    {
        var services = new ServiceCollection();
        services.AddBsftCaching();
        services.AddScoped<ICacheKeyScopeProvider, HostScopeProvider>();

        var result = await new TenantCacheScopeStartupValidator(services).ValidateAsync(CancellationToken.None);

        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task TenantCacheScopeStartupValidator_WhenNothingCaches_StaysSilent()
    {
        var services = new ServiceCollection();

        var result = await new TenantCacheScopeStartupValidator(services)
            .ValidateAsync(CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void AddBsftDataMultiTenancy_WhenCalled_RegistersTheCacheScopeGuard()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(tenancy => tenancy.ResolveFromHeader());

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IStartupValidator>()
            .Should().ContainSingle(validator => validator is TenantCacheScopeStartupValidator);
    }

    [Fact]
    public async Task AddBsftDataMultiTenancy_WhenTheHostAlsoCachesGlobally_ReportsTheTenantBlindScope()
    {
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(tenancy => tenancy.ResolveFromHeader());
        services.AddBsftCaching();

        using var provider = services.BuildServiceProvider();

        var guard = provider.GetServices<IStartupValidator>().OfType<TenantCacheScopeStartupValidator>().Single();
        var result = await guard.ValidateAsync(CancellationToken.None);

        result.Message.Should().Be("Caching runs on the global cache key scope.");
        result.Warnings.Should().ContainSingle();
    }

    /// <summary>A host's own scope provider, whatever it folds in.</summary>
    private sealed class HostScopeProvider : ICacheKeyScopeProvider
    {
        public CacheKeyScope GetScope() => CacheKeyScope.For("host");
    }
}
