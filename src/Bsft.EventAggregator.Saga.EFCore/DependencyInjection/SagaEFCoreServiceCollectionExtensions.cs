using Bsft.EventAggregator.Saga.Persistence;
using Bsft.EventAggregator.Saga.Reading;
using Bsft.EventAggregator.Saga.Timeouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.EventAggregator.Saga.EFCore.DependencyInjection;

/// <summary>
/// Extension methods for persisting saga state, and optionally saga timeouts, with EF Core.
/// </summary>
/// <remarks>
/// Both methods replace registrations <c>AddBsftSaga</c> made, so they are called after it. Until
/// 1.3.5 the same wiring was reached through <c>UseDbProvider&lt;TContext&gt;()</c> on the options
/// object, which made the choice of store a property of a configuration record rather than a
/// registration, and left an <c>IServiceCollection</c> the package could not otherwise reach.
/// </remarks>
public static class SagaEFCoreServiceCollectionExtensions
{
    /// <summary>
    /// Persists saga state with EF Core through the given <see cref="DbContext"/>.
    /// </summary>
    /// <remarks>
    /// Persists saga state only. Durable timeouts keep the in-memory store and would not survive a
    /// restart — use <see cref="AddBsftSagaEFCoreWithTimeouts{TContext}"/> for those.
    /// </remarks>
    /// <typeparam name="TContext">The DbContext type that implements <see cref="ISagaDbContext"/>.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftSagaEFCore<TContext>(this IServiceCollection services)
        where TContext : DbContext, ISagaDbContext
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.AddScoped<ISagaDbContext>(sp => sp.GetRequiredService<TContext>());

        // The operator read side. Instances only: this overload keeps the in-memory timeout store,
        // so there is no timeout table to read beside them.
        services.TryAddScoped<ISagaInstanceReader, EFCoreSagaInstanceReader>();

        // The relational store is registered under its own interface first and reached through the
        // container from there. Constructing it inside the ISagaStore factory instead would leave
        // IEFCoreSagaStore unregistered, so the seam the README offers for replacing exactly the
        // relational store would resolve nothing and a replacement of it would change nothing.
        services.TryAddSingleton<IEFCoreSagaStore>(
            sp => new EFCoreSagaStore(sp.GetRequiredService<IServiceScopeFactory>()));

        services.Replace(ServiceDescriptor.Singleton<ISagaStore>(
            sp => sp.GetRequiredService<IEFCoreSagaStore>()));

        return services;
    }

    /// <summary>
    /// Persists both saga state and durable timeouts with EF Core through the given
    /// <see cref="DbContext"/>.
    /// </summary>
    /// <remarks>
    /// Both stores target the same context type, so this is also the mode in which a step's state
    /// advance and its timeout writes are committed together: the registered
    /// <see cref="EFCoreSagaPersistenceWindow"/> hands both stores one context per persist window.
    /// </remarks>
    /// <typeparam name="TContext">The DbContext type that implements <see cref="ISagaTimeoutDbContext"/>.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftSagaEFCoreWithTimeouts<TContext>(this IServiceCollection services)
        where TContext : DbContext, ISagaTimeoutDbContext
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.AddScoped<ISagaDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<ISagaTimeoutDbContext>(sp => sp.GetRequiredService<TContext>());

        // The operator read side, both halves: with a timeout table there is something to read
        // beside each instance.
        services.TryAddScoped<ISagaInstanceReader, EFCoreSagaInstanceReader>();
        services.TryAddScoped<ISagaTimeoutReader, EFCoreSagaTimeoutReader>();

        // One instance behind both roles: the window is what knows which context is current.
        services.AddSingleton<EFCoreSagaPersistenceWindow>();
        services.Replace(ServiceDescriptor.Singleton<ISagaPersistenceWindow>(
            sp => sp.GetRequiredService<EFCoreSagaPersistenceWindow>()));
        services.TryAddSingleton<ISagaDbContextAccessor>(
            sp => sp.GetRequiredService<EFCoreSagaPersistenceWindow>());

        // Both relational stores go in under their own interfaces and the engine's contracts resolve
        // to whatever answers those seams, for the reason given in AddBsftSagaEFCore above.
        services.TryAddSingleton<IEFCoreSagaStore>(sp => new EFCoreSagaStore(
            sp.GetRequiredService<IServiceScopeFactory>(),
            window: sp.GetRequiredService<ISagaDbContextAccessor>()));

        services.TryAddSingleton<IEFCoreSagaTimeoutStore>(sp => new EFCoreSagaTimeoutStore(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ISagaDbContextAccessor>()));

        services.Replace(ServiceDescriptor.Singleton<ISagaStore>(
            sp => sp.GetRequiredService<IEFCoreSagaStore>()));

        services.Replace(ServiceDescriptor.Singleton<ISagaTimeoutStore>(
            sp => sp.GetRequiredService<IEFCoreSagaTimeoutStore>()));

        return services;
    }
}
