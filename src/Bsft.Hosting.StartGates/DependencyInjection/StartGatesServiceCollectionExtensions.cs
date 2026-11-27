using Bsft.Core.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bsft.Hosting.StartGates.DependencyInjection;

/// <summary>
/// Registers start gates and the hosted loops that wait for them.
/// </summary>
public static class StartGatesServiceCollectionExtensions
{
    /// <summary>
    /// Registers a start gate that every loop registered with <c>AddBsftGatedHostedService</c> awaits
    /// before it starts.
    /// </summary>
    /// <typeparam name="TGate">The gate, constructed by the container as a singleton.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// Several gates may be registered; a loop awaits all of them, in registration order. Registering the
    /// same <typeparamref name="TGate"/> twice registers it once. A gate that also implements
    /// <see cref="IHostedWorkStartGateOverdueSignal"/> is what the opt-in start gate health check reports.
    /// The order of this call against the registration of the loops does not matter: the gates are read
    /// when the host starts.
    /// </remarks>
    public static IServiceCollection AddBsftHostedWorkStartGate<TGate>(this IServiceCollection services)
        where TGate : class, IHostedWorkStartGate
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedWorkStartGate, TGate>());

        return services;
    }

    /// <summary>
    /// Registers a start gate built by a factory, for a gate the host also registers under its own type or
    /// another interface and wants to be one instance.
    /// </summary>
    /// <typeparam name="TGate">The gate.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="factory">Resolves the gate, typically from the registration the host already has.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="factory"/> is null.</exception>
    public static IServiceCollection AddBsftHostedWorkStartGate<TGate>(
        this IServiceCollection services,
        Func<IServiceProvider, TGate> factory)
        where TGate : class, IHostedWorkStartGate
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        services.AddSingleton<IHostedWorkStartGate>(factory);

        return services;
    }

    /// <summary>
    /// Registers a hosted service that starts only after every registered start gate has opened.
    /// </summary>
    /// <typeparam name="TService">The hosted service, constructed by the container as a singleton.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// The replacement for <c>AddHostedService&lt;TService&gt;()</c> for a loop that must not run before the
    /// host's preconditions hold. The service is registered once however often this is called, and is
    /// also resolvable as <typeparamref name="TService"/>.
    /// </para>
    /// <para>
    /// With no gate registered the container hands the service itself to the host, so nothing differs from
    /// <c>AddHostedService</c>. With gates the host gets a wrapper that returns from start at once and
    /// starts the service after the gates. Which gates exist is decided when the host starts, so it does
    /// not matter whether a gate is registered before or after this call.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBsftGatedHostedService<TService>(this IServiceCollection services)
        where TService : class, IHostedService
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<TService>();

        if (services.Any(IsRegistrationOf<TService>))
            return services;

        return services.AddSingleton<IHostedService>(new Registration<TService>().Create);
    }

    /// <summary>
    /// Registers a hosted service built by a factory that starts only after every registered start gate
    /// has opened.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="factory">Builds the service, typically by resolving the registration the host already has.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="factory"/> is null.</exception>
    /// <remarks>
    /// The same as the overload without a factory, for a service the package builds itself, or registers
    /// under its own type as well. The service is built when the host starts. Unlike the typed overload it does
    /// not deduplicate: each call adds one more hosted registration, so a registration that may run twice
    /// should register the service under its own type and use the typed overload.
    /// </remarks>
    public static IServiceCollection AddBsftGatedHostedService(
        this IServiceCollection services,
        Func<IServiceProvider, IHostedService> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        return services.AddSingleton<IHostedService>(provider => Gate(provider, factory(provider)));
    }

    /// <summary>
    /// Says whether <see cref="AddBsftGatedHostedService{TService}(IServiceCollection)"/> registered a hosted service.
    /// </summary>
    /// <typeparam name="TService">The hosted service to look for.</typeparam>
    /// <param name="services">The service collection to look in.</param>
    /// <returns><c>true</c> when the service was registered by the typed overload, <c>false</c> otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// The hosted registration of a gated service is a factory, so its descriptor does not name the service type the
    /// way <c>AddHostedService</c> does; this is the question to ask instead.
    /// </remarks>
    public static bool HasBsftGatedHostedService<TService>(this IServiceCollection services)
        where TService : class, IHostedService
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.Any(IsRegistrationOf<TService>);
    }

    /// <summary>
    /// Removes a hosted service that <see cref="AddBsftGatedHostedService{TService}(IServiceCollection)"/> registered,
    /// so a later registration can replace it.
    /// </summary>
    /// <typeparam name="TService">The hosted service that was registered.</typeparam>
    /// <param name="services">The service collection to remove from.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// Removes the hosted registration and the registration of <typeparamref name="TService"/> under its own type
    /// that came with it. Nothing happens when the service was not registered that way.
    /// </remarks>
    public static IServiceCollection RemoveBsftGatedHostedService<TService>(this IServiceCollection services)
        where TService : class, IHostedService
    {
        ArgumentNullException.ThrowIfNull(services);

        var registrations = services.Where(IsRegistrationOf<TService>).ToArray();
        if (registrations.Length == 0)
            return services;

        foreach (var registration in registrations)
            services.Remove(registration);

        foreach (var own in services.Where(descriptor => !descriptor.IsKeyedService
                     && descriptor.ServiceType == typeof(TService)
                     && descriptor.ImplementationType == typeof(TService)).ToArray())
        {
            services.Remove(own);
        }

        return services;
    }

    private static bool IsRegistrationOf<TService>(ServiceDescriptor descriptor)
        where TService : class, IHostedService =>
        !descriptor.IsKeyedService
        && descriptor.ServiceType == typeof(IHostedService)
        && descriptor.ImplementationFactory?.Target is Registration<TService>;

    private static IHostedService Gate(IServiceProvider provider, IHostedService inner)
    {
        var gates = provider.GetServices<IHostedWorkStartGate>().ToArray();

        if (gates.Length == 0)
            return inner;

        var logger = provider.GetService<ILoggerFactory>()?.CreateLogger<GatedHostedService>() ?? NullLogger<GatedHostedService>.Instance;

        return new GatedHostedService(inner, gates, logger);
    }

    /// <summary>The factory of one typed registration; its type is what tells a second call it was done.</summary>
    private sealed class Registration<TService>
        where TService : class, IHostedService
    {
        public IHostedService Create(IServiceProvider provider) => Gate(provider, provider.GetRequiredService<TService>());
    }
}
