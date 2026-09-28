using Bsft.EventAggregator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Tests.DependencyInjection;

public class EventAggregatorServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftEventAggregator_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftEventAggregator();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddBsftEventAggregator_RegistersIEventAggregatorAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();

        var provider = services.BuildServiceProvider();

        var aggregator1 = provider.GetRequiredService<IEventAggregator>();
        var aggregator2 = provider.GetRequiredService<IEventAggregator>();

        aggregator1.Should().NotBeNull();
        aggregator1.Should().BeSameAs(aggregator2, "it must be a singleton");
        aggregator1.Should().BeOfType<SimpleEventAggregator>();
    }

    [Fact]
    public void AddBsftEventAggregator_AnAggregatorTheHostRegistered_IsLeftInPlace()
    {
        // The seam the README describes: a host that wants delivery ordered, buffered or scheduled
        // registers its own, and calling this afterwards must not quietly take it back.
        var services = new ServiceCollection();
        var own = new SimpleEventAggregator();
        services.AddSingleton<IEventAggregator>(own);

        services.AddBsftEventAggregator();

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEventAggregator>().Should().BeSameAs(own);
    }
}
