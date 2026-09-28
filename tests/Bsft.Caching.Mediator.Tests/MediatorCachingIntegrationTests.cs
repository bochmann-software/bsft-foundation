using Bsft.Caching.DependencyInjection;
using Bsft.Caching.Invalidation;
using Bsft.Caching.Mediator.DependencyInjection;
using Bsft.Mediator;
using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Caching.Mediator.Tests;

/// <summary>
/// End-to-end checks that a cacheable query is served from the cache when sent through the real
/// mediator, and runs its handler again after invalidation.
/// </summary>
public class MediatorCachingIntegrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CountingHandler.Counter>();
        services.AddBsftMediator(c => c.RegisterServicesFromAssembly(typeof(CountingHandler).Assembly));
        services.AddBsftCaching();
        services.AddBsftCachingMediator();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RepeatedQuery_RunsTheHandlerOnce()
    {
        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var counter = provider.GetRequiredService<CountingHandler.Counter>();

        await mediator.QueryAsync(new TaggedQuery("k", null, ["orders"]));
        await mediator.QueryAsync(new TaggedQuery("k", null, ["orders"]));

        counter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task QueryAfterTagInvalidation_RunsTheHandlerAgain()
    {
        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var counter = provider.GetRequiredService<CountingHandler.Counter>();
        var invalidator = provider.GetRequiredService<ICacheInvalidator>();

        await mediator.QueryAsync(new TaggedQuery("k", null, ["orders"]));
        await invalidator.InvalidateTagAsync("orders");
        await mediator.QueryAsync(new TaggedQuery("k", null, ["orders"]));

        counter.Calls.Should().Be(2);
    }

    [Fact]
    public async Task UnmarkedQuery_IsNeverCached()
    {
        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var counter = provider.GetRequiredService<CountingHandler.Counter>();

        await mediator.QueryAsync(new PlainQuery("v"));
        await mediator.QueryAsync(new PlainQuery("v"));

        counter.Calls.Should().Be(2);
    }

    [Fact]
    public async Task CacheableCommand_AlwaysReachesItsHandler()
    {
        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var counter = provider.GetRequiredService<CountingHandler.Counter>();

        await mediator.SendAsync(new CacheableCommand("k"));
        await mediator.SendAsync(new CacheableCommand("k"));

        counter.Calls.Should().Be(2);
    }
}
