using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Testing.Contexts;

/// <summary>
/// The bookkeeping both harness builders share: registrations are collected in the order they were
/// made and applied when the container is assembled.
/// </summary>
/// <remarks>
/// The fluent methods stay on each builder because they return that builder's own interface, but the
/// mechanics and the argument checks behind them live here once. It sits beside
/// <see cref="TestContextRegistrations"/> because both describe what a harness registers into its
/// container; the package root stays free of types, since <c>Bsft.Testing</c> is a namespace 2.0.0
/// removed.
/// </remarks>
internal static class TestServiceRegistrations
{
    /// <summary>Records a registration.</summary>
    /// <param name="registrations">The collected registrations.</param>
    /// <param name="configure">Applied to the container while it is being assembled.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is null.</exception>
    public static void AddConfiguration(
        this List<Action<IServiceCollection>> registrations,
        Action<IServiceCollection> configure)
    {
        if (configure is null)
            throw new ArgumentNullException(nameof(configure));

        registrations.Add(configure);
    }

    /// <summary>Records a single instance to be registered as <typeparamref name="TService"/>.</summary>
    /// <typeparam name="TService">The service type to resolve the instance as.</typeparam>
    /// <param name="registrations">The collected registrations.</param>
    /// <param name="instance">The instance to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instance"/> is null.</exception>
    public static void AddInstance<TService>(
        this List<Action<IServiceCollection>> registrations,
        TService instance)
        where TService : class
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        registrations.Add(services => services.AddSingleton(instance));
    }

    /// <summary>Applies every recorded registration, in order.</summary>
    /// <param name="registrations">The collected registrations.</param>
    /// <param name="services">The collection to apply them to.</param>
    public static void ApplyTo(this List<Action<IServiceCollection>> registrations, IServiceCollection services)
    {
        foreach (var registration in registrations)
        {
            registration(services);
        }
    }
}
