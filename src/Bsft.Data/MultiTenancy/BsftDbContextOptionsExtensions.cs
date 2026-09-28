using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Extension methods for configuring tenant-aware global query filters on a <see cref="ModelBuilder"/>.
/// </summary>
public static class BsftDbContextOptionsExtensions
{
    /// <summary>
    /// Applies global query filters on all entity types implementing <see cref="ITenantEntity"/>,
    /// scoping queries to the tenant the executing context instance is scoped to.
    /// </summary>
    /// <typeparam name="TContext">
    /// The context type. The constraint is the contract: only the context executing the query gets
    /// rewritten into the filter, so an <see cref="ITenantScopedDbContext"/> that is not the context
    /// itself would be captured exactly like the tenant value this method exists to stop capturing.
    /// </typeparam>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="context">The context being built. Pass <c>this</c> from <c>OnModelCreating</c>.</param>
    /// <returns>The model builder for chaining.</returns>
    /// <remarks>
    /// This is the method to use whenever one process serves more than one tenant. The filter reads
    /// the tenant off the context executing the query, so it survives EF Core's model and
    /// compiled-query caches, both of which outlive a request scope. Where the tenant is instead fixed
    /// for the whole process, <see cref="UseStaticTenantFilter"/> says so explicitly.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public static ModelBuilder UseTenantFilter<TContext>(this ModelBuilder modelBuilder, TContext context)
        where TContext : DbContext, ITenantScopedDbContext
    {
        if (modelBuilder is null)
            throw new ArgumentNullException(nameof(modelBuilder));
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        // Typing the constant as the interface rather than as the concrete context type is what makes
        // EF Core replace it with the context executing the query: its rewriter matches any subtree
        // whose static type the context type is assignable to.
        var currentTenantId = Expression.Property(
            Expression.Constant(context, typeof(ITenantScopedDbContext)),
            nameof(ITenantScopedDbContext.CurrentTenantId));

        return ApplyTenantFilters(modelBuilder, currentTenantId);
    }

    /// <summary>
    /// Applies global query filters on all entity types implementing <see cref="ITenantEntity"/>,
    /// scoping queries to the tenant the supplied <paramref name="tenantContext"/> holds.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="tenantContext">The tenant context providing the current tenant identifier.</param>
    /// <returns>The model builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Only correct where the resolved tenant never varies within the process, for example one
    /// deployment per tenant — which is what <c>Static</c> in the name is there to state at the call
    /// site. The filter captures this one <paramref name="tenantContext"/> instance, and EF Core caches
    /// the model that contains it for the lifetime of the context type, so a host that resolves a
    /// different tenant per request keeps filtering on the tenant of the scope that happened to build
    /// the model first. Use <see cref="UseTenantFilter{TContext}(ModelBuilder, TContext)"/> there.
    /// </para>
    /// <para>
    /// The name differs deliberately rather than overloading <c>UseTenantFilter</c>: the two differed
    /// only in the static type of one argument, so a host that passed its <see cref="ITenantContext"/>
    /// field instead of the context compiled, started, served, and leaked across tenants with nothing
    /// to signal it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public static ModelBuilder UseStaticTenantFilter(this ModelBuilder modelBuilder, ITenantContext tenantContext)
    {
        if (modelBuilder is null)
            throw new ArgumentNullException(nameof(modelBuilder));
        if (tenantContext is null)
            throw new ArgumentNullException(nameof(tenantContext));

        var currentTenantId = Expression.Property(
            Expression.Constant(tenantContext),
            nameof(ITenantContext.TenantId));

        return ApplyTenantFilters(modelBuilder, currentTenantId);
    }

    private static ModelBuilder ApplyTenantFilters(ModelBuilder modelBuilder, Expression currentTenantId)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var tenantIdProperty = Expression.Property(parameter, nameof(ITenantEntity.TenantId));

            var filter = Expression.Lambda(
                Expression.Equal(tenantIdProperty, currentTenantId),
                parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }

        return modelBuilder;
    }
}
