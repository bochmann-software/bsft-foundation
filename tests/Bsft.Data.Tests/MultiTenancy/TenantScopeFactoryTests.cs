using Bsft.Core.Tenancy;
using Bsft.Data.DependencyInjection;
using Bsft.Data.MultiTenancy;
using Bsft.Data.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

public class TenantScopeFactoryTests
{
    private static IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public void CreateScope_WithTenantId_ReturnsScope()
    {
        var factory = new TenantScopeFactory(CreateScopeFactory());

        var scope = factory.CreateScope("t1");

        scope.TenantId.Should().Be("t1");
        scope.Should().NotBeNull();
    }

    [Fact]
    public void CreateScope_ScopeServiceProviderHasTenantContextSet()
    {
        var factory = new TenantScopeFactory(CreateScopeFactory());

        var scope = factory.CreateScope("t1");

        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.TenantId.Should().Be("t1");
    }

    [Fact]
    public void CreateScope_TwoScopesForDifferentTenants_AreIndependent()
    {
        var factory = new TenantScopeFactory(CreateScopeFactory());

        var scope1 = factory.CreateScope("t1");
        var scope2 = factory.CreateScope("t2");

        var tenantContext1 = scope1.ServiceProvider.GetRequiredService<ITenantContext>();
        var tenantContext2 = scope2.ServiceProvider.GetRequiredService<ITenantContext>();

        tenantContext1.TenantId.Should().Be("t1");
        tenantContext2.TenantId.Should().Be("t2");
    }

    [Fact]
    public void CreateScope_Dispose_DisposesScope()
    {
        var factory = new TenantScopeFactory(CreateScopeFactory());

        var scope = factory.CreateScope("t1");
        scope.Dispose();

        // After dispose, Dispose completes without throwing
    }

    [Fact]
    public async Task CreateScope_DisposeAsync_DisposesScope()
    {
        var factory = new TenantScopeFactory(CreateScopeFactory());

        var scope = factory.CreateScope("t1");

        await scope.DisposeAsync();

        // DisposeAsync should not throw
    }

    [Fact]
    public void CreateScope_NullTenantId_Throws()
    {
        var factory = new TenantScopeFactory(CreateScopeFactory());

        var act = () => factory.CreateScope(null!);

        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void CreateScope_EmptyTenantId_Throws()
    {
        var factory = new TenantScopeFactory(CreateScopeFactory());

        var act = () => factory.CreateScope("");

        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void CreateScope_WhitespaceTenantId_Throws()
    {
        var factory = new TenantScopeFactory(CreateScopeFactory());

        var act = () => factory.CreateScope("   ");

        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void AddMultiTenancy_RegistersITenantScopeFactoryAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(config => { });

        var provider = services.BuildServiceProvider();
        var factory1 = provider.GetRequiredService<ITenantScopeFactory>();
        var factory2 = provider.GetRequiredService<ITenantScopeFactory>();

        factory1.Should().BeSameAs(factory2);
    }

    [Fact]
    public void AddMultiTenancy_WithPreregisteredITenantScopeFactory_DoesNotReplace()
    {
        var services = new ServiceCollection();
        var customFactory = Substitute.For<ITenantScopeFactory>();
        services.AddSingleton(customFactory);

        services.AddBsftDataMultiTenancy(config => { });

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ITenantScopeFactory>();

        factory.Should().BeSameAs(customFactory);
    }

    [Fact]
    public void CreateScope_WithATenantContextThatCannotBeSet_ThrowsAndDisposesTheScope()
    {
        var services = new ServiceCollection();
        var disposalProbes = new List<DisposalProbe>();

        services.AddScoped<DisposalProbe>(sp =>
        {
            var probe = new DisposalProbe();
            disposalProbes.Add(probe);
            return probe;
        });
        services.AddScoped<ITenantContext>(sp =>
        {
            sp.GetRequiredService<DisposalProbe>();
            return Substitute.For<ITenantContext>();
        });

        var provider = services.BuildServiceProvider();
        var scopeFactory = new TenantScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());

        var act = () => scopeFactory.CreateScope("a");

        act.Should().Throw<InvalidOperationException>();
        disposalProbes.Should().HaveCount(1);
        disposalProbes[0].IsDisposed.Should().BeTrue();
    }
}
