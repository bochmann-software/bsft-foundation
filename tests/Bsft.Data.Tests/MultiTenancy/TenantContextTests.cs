using Bsft.Core.Tenancy;
using Bsft.Data.DependencyInjection;
using Bsft.Data.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

public class TenantContextTests
{
    [Fact]
    public void TenantContext_InitialState_IsNotResolved()
    {
        var context = new TenantContext();

        context.IsResolved.Should().BeFalse();
        context.TenantId.Should().BeNull();
    }

    [Fact]
    public void TenantContext_AfterSet_IsResolved()
    {
        var context = new TenantContext();

        context.SetTenantId("tenant-1");

        context.IsResolved.Should().BeTrue();
        context.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public void TenantContext_SetNull_IsResolvedButNull()
    {
        var context = new TenantContext();

        context.SetTenantId(null);

        context.IsResolved.Should().BeTrue();
        context.TenantId.Should().BeNull();
    }

    [Fact]
    public void TenantId_WithContextCreatedInAPass_SeesThatTenant()
    {
        TenantContext context;

        using (new BackgroundTenant("t1"))
        {
            context = new TenantContext();
            context.TenantId.Should().Be("t1");
            context.IsResolved.Should().BeTrue();
        }
    }

    [Fact]
    public void TenantId_WithContextCreatedInAPass_KeepsThatTenantAfterThePassEnds()
    {
        TenantContext context;

        using (new BackgroundTenant("t1"))
        {
            context = new TenantContext();
            context.TenantId.Should().Be("t1");
        }

        context.TenantId.Should().Be("t1");
        context.IsResolved.Should().BeTrue();
    }

    [Fact]
    public void TenantContext_ExplicitlyResolved_IgnoresBackgroundTenant()
    {
        var context = new TenantContext();
        context.SetTenantId("t2");

        using (new BackgroundTenant("t1"))
        {
            context.TenantId.Should().Be("t2");
            context.IsResolved.Should().BeTrue();
        }
    }

    [Fact]
    public void TenantId_WithAContextCreatedOutsideAPassAndReadInsideOne_StaysWithoutATenant()
    {
        var context = new TenantContext();

        context.IsResolved.Should().BeFalse();
        context.TenantId.Should().BeNull();

        using (new BackgroundTenant("a"))
        {
            context.TenantId.Should().BeNull();
            context.IsResolved.Should().BeFalse();
        }
    }

    [Fact]
    public void TenantId_WithAContextCreatedInOnePassAndReadInAnother_KeepsTheFirstTenant()
    {
        TenantContext context;

        using (new BackgroundTenant("a"))
        {
            context = new TenantContext();
            context.TenantId.Should().Be("a");
            context.IsResolved.Should().BeTrue();
        }

        using (new BackgroundTenant("b"))
        {
            context.TenantId.Should().Be("a");
            context.IsResolved.Should().BeTrue();
        }
    }

    [Fact]
    public void TenantId_WithAScopeCreatedOutsideAPassAndUsedInsideOne_StaysWithoutATenant()
    {
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(config => { });
        var serviceProvider = services.BuildServiceProvider();

        var scope = serviceProvider.CreateScope();
        var contextFromFirstResolve = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        contextFromFirstResolve.IsResolved.Should().BeFalse();
        contextFromFirstResolve.TenantId.Should().BeNull();

        using (new BackgroundTenant("a"))
        {
            var contextFromSecondResolve = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            contextFromFirstResolve.TenantId.Should().BeNull();
            contextFromFirstResolve.IsResolved.Should().BeFalse();
            contextFromSecondResolve.TenantId.Should().BeNull();
            contextFromSecondResolve.IsResolved.Should().BeFalse();
        }

        scope.Dispose();
    }

    [Fact]
    public void TenantId_WithAScopeCreatedInsideAPassAndResolvedInThatPass_SeesThatTenant()
    {
        using (new BackgroundTenant("a"))
        {
            var services = new ServiceCollection();
            services.AddBsftDataMultiTenancy(config => { });
            var serviceProvider = services.BuildServiceProvider();

            var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            context.TenantId.Should().Be("a");
            context.IsResolved.Should().BeTrue();

            scope.Dispose();
        }
    }

    [Fact]
    public void SetTenantId_WithAContextCreatedInAPass_OverridesTheBackgroundTenant()
    {
        TenantContext context;

        using (new BackgroundTenant("a"))
        {
            context = new TenantContext();
            context.TenantId.Should().Be("a");
            context.IsResolved.Should().BeTrue();

            context.SetTenantId("b");

            context.TenantId.Should().Be("b");
            context.IsResolved.Should().BeTrue();
        }

        context.TenantId.Should().Be("b");
        context.IsResolved.Should().BeTrue();
    }
}
