using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Mediator.EventAggregator.DependencyInjection;

/// <summary>
/// Extension methods for registering the <see cref="EventAggregatorNotificationForwarder"/>.
/// </summary>
public static class MediatorEventAggregatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="EventAggregatorNotificationForwarder"/>, which carries every
    /// notification published through the mediator into the in-process event aggregator. Requires
    /// the event aggregator to be registered and the mediator to be configured with
    /// <c>ForwardNotifications()</c>; without that call the forwarder is never invoked.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftMediatorEventAggregator(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<INotificationForwarder, EventAggregatorNotificationForwarder>();

        return services;
    }
}
