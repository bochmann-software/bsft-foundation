using Bsft.Data.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

public sealed class TenantContextScopeExtensionsTests
{
    [Fact]
    public void EnterTenantScope_WhenTheServicesAreNull_Refuses()
    {
        var act = () => ((IServiceProvider)null!).EnterTenantScope("acme", _ => "irrelevant");

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void EnterTenantScope_WhenTheTenantIdIsNull_Refuses()
    {
        var provider = new ServiceCollection().BuildServiceProvider();

        var act = () => provider.EnterTenantScope(null!, _ => "irrelevant");

        act.Should().Throw<ArgumentNullException>().WithParameterName("tenantId");
    }

    [Fact]
    public void EnterTenantScope_WhenTheFailureDescriberIsNull_Refuses()
    {
        var provider = new ServiceCollection().BuildServiceProvider();

        var act = () => provider.EnterTenantScope("acme", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("describeFailure");
    }

    [Fact]
    public void EnterTenantScope_WhenASettableContextIsRegistered_StatesTheTenantOnIt()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.EnterTenantScope("acme", _ => "irrelevant");

        scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId.Should().Be("acme");
    }

    [Fact]
    public void EnterTenantScope_WhenNoContextIsRegistered_RefusesWithTheDescribedMessage()
    {
        var provider = new ServiceCollection().BuildServiceProvider();

        var act = () => provider.EnterTenantScope(
            "acme", context => $"resolved: {context?.GetType().FullName ?? "not registered"}");

        act.Should().Throw<InvalidOperationException>().WithMessage("resolved: not registered");
    }

    [Fact]
    public void EnterTenantScope_WhenTheRegisteredContextIsNotSettable_RefusesWithTheDescribedMessage()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext>(_ => new ReadOnlyTenantContext("acme"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.EnterTenantScope(
            "acme", context => $"resolved: {context?.GetType().Name}");

        act.Should().Throw<InvalidOperationException>().WithMessage("resolved: ReadOnlyTenantContext");
    }

    /// <summary>An <see cref="ITenantContext"/> that is not the shipped settable implementation.</summary>
    private sealed class ReadOnlyTenantContext : ITenantContext
    {
        internal ReadOnlyTenantContext(string? tenantId) => TenantId = tenantId;

        /// <inheritdoc />
        public string? TenantId { get; }

        /// <inheritdoc />
        public bool IsResolved => TenantId is not null;
    }
}
