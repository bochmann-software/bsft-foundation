using Bsft.Scheduling.Reading;
using Bsft.Scheduling.StoredSchedules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Scheduling.EFCore.DependencyInjection;

/// <summary>
/// Extension methods for persisting the job scheduler's runs with EF Core.
/// </summary>
public static class SchedulingEFCoreServiceCollectionExtensions
{
    /// <summary>
    /// Stores job runs in the given DbContext, so a delayed job survives a restart and several
    /// instances coordinate through the database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces the in-memory <see cref="IJobStore"/> that <c>AddBsftScheduling</c> registers.
    /// Either order works: the core registers its default with <c>TryAdd</c> and this one removes
    /// whatever is there before registering the EF Core store.
    /// </para>
    /// <para>
    /// The context has to apply <see cref="JobRunEntityConfiguration"/> — through
    /// <c>ApplyConfigurationsFromAssembly</c> or an explicit <c>ApplyConfiguration</c> call — and the
    /// resulting table needs a migration. The context itself is registered by the host, with
    /// <c>AddDbContext&lt;TContext&gt;</c>; this only maps <see cref="IJobDbContext"/> onto it.
    /// </para>
    /// </remarks>
    /// <typeparam name="TContext">The DbContext type that implements <see cref="IJobDbContext"/>.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftSchedulingEFCore<TContext>(this IServiceCollection services)
        where TContext : DbContext, IJobDbContext
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.AddScoped<IJobDbContext>(sp => sp.GetRequiredService<TContext>());
        services.TryAddSingleton<IJobRunMapper, JobRunMapper>();

        // A plain Add would leave the in-memory default in the collection ahead of this one and let
        // the call order decide which store the container resolves.
        services.RemoveAll<IJobStore>();
        services.AddSingleton<IJobStore, EFCoreJobStore>();

        // The operator read side hangs off the same context. It is scoped rather than a singleton
        // because it answers a request, which already has a scope, rather than a poll loop. There is
        // no in-memory counterpart on purpose: a console asking what failed last night is asking about
        // runs that outlive the process.
        services.TryAddScoped<IJobRunReader, EFCoreJobRunReader>();

        return services;
    }

    /// <summary>
    /// Stores the persisted, per-tenant schedule catalog in the given DbContext, so a stored schedule
    /// survives a restart and several instances coordinate through the database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces the in-memory <see cref="IStoredScheduleStore"/> that <c>AddBsftScheduling</c>
    /// registers. Either order works, as with <see cref="AddBsftSchedulingEFCore{TContext}"/> above.
    /// </para>
    /// <para>
    /// The context has to apply <see cref="StoredScheduleEntityConfiguration"/> and the resulting table
    /// needs a migration. It is deliberately a separate context interface from
    /// <see cref="IJobDbContext"/>: a host that only wants delayed and recurring jobs takes that one
    /// alone, and adding this table to it later needs no change to a context that already implements
    /// <see cref="IJobDbContext"/>.
    /// </para>
    /// </remarks>
    /// <typeparam name="TContext">The DbContext type that implements <see cref="IStoredScheduleDbContext"/>.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftStoredSchedulesEFCore<TContext>(this IServiceCollection services)
        where TContext : DbContext, IStoredScheduleDbContext
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.AddScoped<IStoredScheduleDbContext>(sp => sp.GetRequiredService<TContext>());
        services.TryAddSingleton<IStoredScheduleMapper, StoredScheduleMapper>();

        services.RemoveAll<IStoredScheduleStore>();
        services.AddSingleton<IStoredScheduleStore, EFCoreStoredScheduleStore>();

        return services;
    }
}
