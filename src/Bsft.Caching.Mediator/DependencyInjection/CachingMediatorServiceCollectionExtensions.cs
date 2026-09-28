using Bsft.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Caching.Mediator.DependencyInjection;

/// <summary>
/// Extension methods for registering the caching mediator behavior.
/// </summary>
public static class CachingMediatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CachingBehavior{TRequest, TResponse}"/> as a mediator pipeline behavior,
    /// serving queries marked with <see cref="Bsft.Caching.ICacheable"/> from the cache
    /// store and writing the handler's result back on a miss. Requires the caching core
    /// (<c>AddBsftCaching</c>) to be registered, because the behavior resolves the store, the key
    /// builder and the tag index from it.
    /// </summary>
    /// <remarks>
    /// Call this after <c>AddBsftMediator</c> and after every authorization behavior. The mediator
    /// runs behaviors in registration order, innermost last, so a behavior registered after this one
    /// no longer runs on a cache hit — registering an authorization behavior last would let a hit
    /// skip the permission check. Calling this twice registers a single behavior.
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftCachingMediator(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddEnumerable(
            ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>)));

        return services;
    }
}
