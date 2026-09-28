using Bsft.Data.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

/// <summary>Tests for <see cref="TenantWorkScopeExtensions"/>.</summary>
public sealed class TenantWorkScopeExtensionsTests
{
    private const string Work = "The nightly sweep";

    private readonly ServiceCollection _services = new();

    [Fact]
    public void CurrentTenant_NullServices_Throws()
    {
        var act = () => ((IServiceProvider)null!).CurrentTenant();

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void CurrentTenant_HostWithoutMultiTenancy_AnswersNoTenant()
    {
        // Multi-tenancy is opt-in, and a host without it records no tenant rather than failing.
        using var provider = _services.BuildServiceProvider();

        provider.CurrentTenant().Should().BeNull();
    }

    [Fact]
    public void CurrentTenant_ScopeServingATenant_AnswersIt()
    {
        GivenSettableContext();
        using var provider = _services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.EnterTenant("acme", Work);

        scope.ServiceProvider.CurrentTenant().Should().Be("acme");
    }

    [Fact]
    public void EnterTenant_NullArgument_Throws()
    {
        using var provider = _services.BuildServiceProvider();

        var services = () => ((IServiceProvider)null!).EnterTenant("acme", Work);
        var work = () => provider.EnterTenant("acme", null!);

        services.Should().Throw<ArgumentNullException>().WithParameterName("services");
        work.Should().Throw<ArgumentNullException>().WithParameterName("work");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EnterTenant_NoTenantToState_LeavesTheScopeUnresolved(string? tenantId)
    {
        // Empty counts as absent: a context told it is resolved to "" is worse than an unresolved one,
        // because everything downstream then reads it as a real tenant. Asserted without any context
        // registered, so a call that tried to state one would have thrown.
        using var provider = _services.BuildServiceProvider();

        var act = () => provider.EnterTenant(tenantId, Work);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnterTenant_SettableContext_TellsItTheTenant()
    {
        GivenSettableContext();
        using var provider = _services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.EnterTenant("acme", Work);

        scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId.Should().Be("acme");
    }

    [Fact]
    public void EnterTenant_ContextThatCannotBeTold_ThrowsNamingTheWorkAndTheTenant()
    {
        _services.AddScoped(_ => Substitute.For<ITenantContext>());
        using var provider = _services.BuildServiceProvider();

        var act = () => provider.EnterTenant("acme", Work);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{Work}*'acme'*AddBsftDataMultiTenancy*");
    }

    [Fact]
    public void EnterTenant_ContextThatCannotBeTold_NamesTheRegisteredTypeInFull()
    {
        _services.AddScoped<ITenantContext, FixedTenantContext>();
        using var provider = _services.BuildServiceProvider();

        var act = () => provider.EnterTenant("acme", Work);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*'{typeof(FixedTenantContext).FullName}'*");
    }

    [Fact]
    public void EnterTenant_NoContextAtAll_SaysSo()
    {
        using var provider = _services.BuildServiceProvider();

        var act = () => provider.EnterTenant("acme", Work);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'none'*");
    }

    [Fact]
    public void RequireSettableTenantContext_NullArgument_Throws()
    {
        using var provider = _services.BuildServiceProvider();

        var services = () => ((IServiceProvider)null!).RequireSettableTenantContext(Work);
        var work = () => provider.RequireSettableTenantContext(null!);

        services.Should().Throw<ArgumentNullException>().WithParameterName("services");
        work.Should().Throw<ArgumentNullException>().WithParameterName("work");
    }

    [Fact]
    public void RequireSettableTenantContext_SettableContext_StatesNoTenantOfItsOwn()
    {
        // The point of the probe: it proves the context could be told, without marking it resolved to
        // a tenant that does not exist.
        GivenSettableContext();
        using var provider = _services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.RequireSettableTenantContext(Work);

        scope.ServiceProvider.CurrentTenant().Should().BeNull();
    }

    [Fact]
    public void RequireSettableTenantContext_ContextThatCannotBeTold_ThrowsWithoutNamingATenant()
    {
        _services.AddScoped(_ => Substitute.For<ITenantContext>());
        using var provider = _services.BuildServiceProvider();

        var act = () => provider.RequireSettableTenantContext(Work);

        act.Should().Throw<InvalidOperationException>().WithMessage("*the tenant it is serving*");
    }

    /// <summary>Registers the shipped context, the only one that can be told which tenant it serves.</summary>
    private void GivenSettableContext()
    {
        _services.AddScoped<TenantContext>();
        _services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
    }
}
