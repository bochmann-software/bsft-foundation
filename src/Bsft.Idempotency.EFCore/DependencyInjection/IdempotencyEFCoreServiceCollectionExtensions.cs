using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Idempotency.EFCore.DependencyInjection;

/// <summary>
/// Registers the shared idempotency store and the pruner that keeps its table bounded.
/// </summary>
public static class IdempotencyEFCoreServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the in-process idempotency store with one over a shared table on the application's
    /// own context, and registers the pruner.
    /// </summary>
    /// <typeparam name="TContext">
    /// The application's context, which implements <see cref="IIdempotencyDbContext"/>.
    /// </typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This is what a host running more than one replica needs. The default store
    /// <c>AddBsftIdempotency</c> registers remembers keys in one process, so a retry that lands on
    /// another instance runs: the duplicate window it leaves open is multiplied by the number of
    /// replicas. A table has one arbiter however many processes ask.
    /// </para>
    /// <para>
    /// It <em>replaces</em> rather than tries to add, so it does not matter whether it is called
    /// before or after <c>AddBsftIdempotency</c> — which of two stores is in effect must not depend
    /// on the order two lines of start-up code happen to be in.
    /// </para>
    /// <para>
    /// The context is the application's, so its <c>OnModelCreating</c> has to apply
    /// <see cref="IdempotencyRecordEntityConfiguration"/>. Leaving that to convention maps the key
    /// columns with no unique index and no length, and the store is then there and protects nothing.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftIdempotencyEFCore<TContext>(this IServiceCollection services)
        where TContext : class, IIdempotencyDbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IIdempotencyDbContext>(provider => provider.GetRequiredService<TContext>());

        services.RemoveAll<IIdempotencyStore>();
        services.AddScoped<IIdempotencyStore, EFCoreIdempotencyStore>();

        services.AddHostedService<IdempotencyRecordSweeper>();

        return services;
    }
}
