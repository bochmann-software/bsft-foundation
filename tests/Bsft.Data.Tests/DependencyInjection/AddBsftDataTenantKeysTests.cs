using Bsft.Core.Persistence;
using Bsft.Data.DependencyInjection;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Tests.DependencyInjection;

/// <summary>
/// Tests for AddBsftDataTenantKeys registration and options validation.
/// </summary>
public sealed class AddBsftDataTenantKeysTests
{
    [Fact]
    public void AddBsftDataTenantKeys_WithoutConfigure_RegistersTheRandomNodeGeneratorOverTheSystemClock()
    {
        var services = new ServiceCollection();
        services.AddBsftDataTenantKeys();

        using var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<ITimeOrderedIdGenerator>();

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
        generator.Should().Be(TimeOrderedIdGenerator.ForRandomNode(TimeProvider.System));
    }

    [Fact]
    public void AddBsftDataTenantKeys_WhenTheHostRegisteredAClock_ReadsTheTimeFieldFromIt()
    {
        var now = TimeOrderedIdGenerator.Epoch.AddDays(10);
        var clock = new ManualTimeProvider(now);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddBsftDataTenantKeys(o => o.NodeId = 7);

        using var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<ITimeOrderedIdGenerator>();

        generator.Should().BeSameAs(TimeOrderedIdGenerator.ForNode(7, clock));
        TimeOrderedIdGenerator.Decompose(generator.NextId()).CreatedAt.Should().Be(now);
    }

    [Fact]
    public void AddBsftDataTenantKeys_WithNodeId_RegistersForNodeGenerator()
    {
        var services = new ServiceCollection();
        services.AddBsftDataTenantKeys(o => o.NodeId = 7);

        using var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<ITimeOrderedIdGenerator>();

        generator.NodeId.Should().Be(7);
        generator.Should().Be(TimeOrderedIdGenerator.ForNode(7, TimeProvider.System));
    }

    [Fact]
    public void AddBsftDataTenantKeys_MultipleCallsSameNodeId_RegistersOnce()
    {
        var services = new ServiceCollection();
        services.AddBsftDataTenantKeys(o => o.NodeId = 10);

        using var provider = services.BuildServiceProvider();
        var generator1 = provider.GetRequiredService<ITimeOrderedIdGenerator>();
        var generator2 = provider.GetRequiredService<ITimeOrderedIdGenerator>();

        generator1.Should().BeSameAs(generator2);
    }

    [Fact]
    public void AddBsftDataTenantKeys_WithNullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftDataTenantKeys();

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftDataTenantKeys_GeneratedIdDecomposesWithCorrectNode()
    {
        var services = new ServiceCollection();
        services.AddBsftDataTenantKeys(o => o.NodeId = 55);

        using var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<ITimeOrderedIdGenerator>();

        var id = generator.NextId();
        var (_, nodeId, _) = TimeOrderedIdGenerator.Decompose(id);

        nodeId.Should().Be(55);
    }

    [Fact]
    public void BsftDataOptions_TenantKeys_NodeIdZero_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddBsftDataTenantKeys(o => o.NodeId = 0);

        using var provider = services.BuildServiceProvider();

        var generator = provider.GetRequiredService<ITimeOrderedIdGenerator>();

        generator.NodeId.Should().Be(0);
    }

    [Fact]
    public void BsftDataOptions_TenantKeys_MaxNodeId_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddBsftDataTenantKeys(o => o.NodeId = TimeOrderedIdGenerator.MaxNodeId);

        using var provider = services.BuildServiceProvider();

        var generator = provider.GetRequiredService<ITimeOrderedIdGenerator>();

        generator.NodeId.Should().Be(TimeOrderedIdGenerator.MaxNodeId);
    }
}
