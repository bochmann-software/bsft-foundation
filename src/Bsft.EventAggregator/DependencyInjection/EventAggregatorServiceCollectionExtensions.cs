using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.EventAggregator.DependencyInjection;

/// <summary>
/// Extension methods for registering EventAggregator services with the dependency injection container.
/// </summary>
public static class EventAggregatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SimpleEventAggregator"/> as the singleton implementation of <see cref="IEventAggregator"/>.
    /// </summary>
    /// <remarks>
    /// An aggregator the host registered itself is left in place. That is what makes the seam the
    /// README describes real: a host that wants delivery ordered, buffered or handed to a scheduler
    /// registers its own and calls this anyway, directly or through a package that calls it.
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBsftEventAggregator(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<IEventAggregator, SimpleEventAggregator>();
        return services;
    }
}
