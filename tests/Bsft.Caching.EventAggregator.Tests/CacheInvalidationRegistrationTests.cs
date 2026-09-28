using Bsft.Caching.EventAggregator.DependencyInjection;
using Bsft.Caching.Invalidation;
using Bsft.EventAggregator;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Bsft.Caching.EventAggregator.Tests;

public class CacheInvalidationRegistrationTests
{
    [Fact]
    public void AddBsftCacheInvalidation_NullServices_Throws()
    {
        var act = () => CachingEventAggregatorServiceCollectionExtensions.AddBsftCacheInvalidation(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftCacheInvalidation_RegistersTheSubscriberAsHostedService()
    {
        using var provider = BuildProvider();

        provider.GetServices<IHostedService>()
            .Should().ContainSingle().Which.Should().BeOfType<CacheInvalidationSubscriber>();
    }

    [Fact]
    public void AddBsftCacheInvalidation_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEventAggregator, SimpleEventAggregator>();
        services.AddSingleton(Substitute.For<ICacheInvalidator>());

        services.AddBsftCacheInvalidation();
        services.AddBsftCacheInvalidation();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().Should().ContainSingle();
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEventAggregator, SimpleEventAggregator>();
        services.AddSingleton(Substitute.For<ICacheInvalidator>());
        services.AddBsftCacheInvalidation();

        return services.BuildServiceProvider();
    }
}
