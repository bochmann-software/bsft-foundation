using Bsft.Caching.DependencyInjection;
using Bsft.Caching.Mediator.DependencyInjection;
using Bsft.Mediator;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Caching.Mediator.Tests.DependencyInjection;

public class CachingMediatorServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftCachingMediator_NullServices_Throws()
    {
        var act = () => CachingMediatorServiceCollectionExtensions.AddBsftCachingMediator(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftCachingMediator_RegistersTheBehavior()
    {
        using var provider = new ServiceCollection()
            .AddBsftCaching()
            .AddBsftCachingMediator()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IPipelineBehavior<DefaultsQuery, string>>()
            .Should().ContainSingle().Which.Should()
            .BeOfType<CachingBehavior<DefaultsQuery, string>>();
    }

    [Fact]
    public void AddBsftCachingMediator_IsIdempotent()
    {
        var services = new ServiceCollection().AddBsftCaching();

        services.AddBsftCachingMediator();
        services.AddBsftCachingMediator();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IPipelineBehavior<DefaultsQuery, string>>().Should().ContainSingle();
    }

    [Fact]
    public void AddBsftCachingMediator_ResolvesWithoutACaptiveDependency()
    {
        // The key builder and its scope provider are scoped, so a behavior resolved outside a scope
        // — or a singleton capturing them — is what scope validation catches here.
        var services = new ServiceCollection().AddBsftCaching().AddBsftCachingMediator();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IPipelineBehavior<DefaultsQuery, string>>();

        act.Should().NotThrow();
    }
}
