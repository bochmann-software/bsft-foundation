using Bsft.Caching;
using Bsft.Caching.Invalidation;
using Bsft.Caching.Keys;
using Bsft.Resilience.Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Bsft.Mediator.Tests.Support;

/// <summary>
/// Registers what the Foundation behaviors of the canonical default chain need to be activated,
/// standing in for the AddBsft* call of each behavior package. The chain is what is under test here,
/// not the behaviors themselves, so every dependency is a substitute.
/// </summary>
/// <remarks>
/// Only the behavior packages of the Foundation tier are referenced by this suite, so authorization,
/// organization authorization and audit resolve to nothing and are skipped, as they are in a host
/// without those packages. <c>Bsft.Mediator.Scenarios.Tests</c> registers the full chain.
/// </remarks>
public static class DefaultBehaviorDependencies
{
    /// <summary>
    /// Registers a substitute for every dependency the Foundation behaviors of the default chain are
    /// activated with, so a container carrying all of them can be built without any real
    /// infrastructure.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddDefaultBehaviorDependencies(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddOptions();

        // Bsft.Resilience.Mediator — RetryBehavior.
        services.AddSingleton(Substitute.For<IMediatorResiliencePipelineProvider>());

        // Bsft.Caching.Mediator — CachingBehavior.
        services.AddSingleton(Substitute.For<ICacheStore>());
        services.AddSingleton(Substitute.For<ICacheKeyBuilder>());
        services.AddSingleton(Substitute.For<ICacheTagIndex>());
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
