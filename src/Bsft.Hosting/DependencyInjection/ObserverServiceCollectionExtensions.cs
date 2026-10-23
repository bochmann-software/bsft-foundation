using Bsft.Core.Observers;
using Bsft.Hosting.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>Extension methods for registering observers and the guard that contains their failures.</summary>
public static class ObserverServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IObserverGuard"/>, an <see cref="IObserverFailureReporter"/> that logs
    /// at warning level, and the open generic <see cref="ObserverComposite{TObserver}"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Adjusts <see cref="ObserverFailureLoggingOptions"/>; it may run more than once.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Every registration is a <c>TryAdd</c>, so a host that registered its own reporter or guard
    /// first keeps it, and the method can be called by every package that has observers. The
    /// composite resolves to every observer registered for its contract, in registration order. The
    /// logging reporter names the observer and the exception type; the exception itself is logged
    /// only when <see cref="ObserverFailureLoggingOptions.IncludeExceptionDetails"/> is set.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftObservers(this IServiceCollection services, Action<ObserverFailureLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<ObserverFailureLoggingOptions>();
        if (configure is not null)
            options.Configure(configure);

        services.TryAddSingleton<IObserverFailureReporter, LoggingObserverFailureReporter>();
        services.TryAddSingleton<IObserverGuard, ObserverGuard>();
        services.TryAddSingleton(typeof(ObserverComposite<>));

        return services;
    }

    /// <summary>
    /// Registers one observer of <typeparamref name="TObserver"/>, together with what
    /// <see cref="AddBsftObservers"/> registers.
    /// </summary>
    /// <typeparam name="TObserver">The observer contract.</typeparam>
    /// <typeparam name="TImplementation">The observer.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Uses <c>TryAddEnumerable</c>: an implementation registered twice is registered once, and the
    /// order of the first registrations is the order the observers are told. Observers are
    /// singletons, because the <see cref="ObserverComposite{TObserver}"/> that holds them is one;
    /// a contract that is already registered as scoped or transient (not keyed) is refused rather than
    /// captured by the composite. Only registrations that exist when this is called are seen: a
    /// scoped registration of the contract added afterwards is caught by scope validation, which the
    /// development host enables, not here.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="TObserver"/> is already registered with a lifetime other than singleton.</exception>
    public static IServiceCollection AddBsftObserver<TObserver, TImplementation>(this IServiceCollection services)
        where TObserver : class
        where TImplementation : class, TObserver
    {
        ArgumentNullException.ThrowIfNull(services);

        var shorter = services.FirstOrDefault(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(TObserver) && descriptor.Lifetime != ServiceLifetime.Singleton);
        if (shorter is not null)
            throw new InvalidOperationException($"{typeof(TObserver).Name} is registered as {shorter.Lifetime}, but observers are told by a singleton and must be singletons themselves.");

        services.AddBsftObservers();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<TObserver, TImplementation>());

        return services;
    }
}
