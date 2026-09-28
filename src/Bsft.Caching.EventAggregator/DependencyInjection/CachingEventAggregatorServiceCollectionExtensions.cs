using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Bsft.Caching.EventAggregator.DependencyInjection;

/// <summary>
/// Extension methods for registering the <see cref="CacheInvalidationSubscriber"/>.
/// </summary>
public static class CachingEventAggregatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="CacheInvalidationSubscriber"/>, which evicts cached query results
    /// in response to invalidation events published through the in-process event aggregator.
    /// Requires <c>AddBsftCaching</c> and the event aggregator to be registered.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftCacheInvalidation(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CacheInvalidationSubscriber>());

        return services;
    }
}
