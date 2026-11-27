using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Extension methods for configuring tenant-aware global query filters on a <see cref="ModelBuilder"/>.
/// </summary>
public static class BsftDbContextOptionsExtensions
{
#if NET10_0_OR_GREATER
    /// <summary>
    /// The name <see cref="UseNamedTenantFilter{TContext}(ModelBuilder, TContext, string)"/> and
    /// <see cref="UseNamedStaticTenantFilter"/> give the tenant filter unless told otherwise.
    /// </summary>
    public const string TenantFilterName = "bsft.tenant";
#endif

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

        return ApplyTenantFilters(modelBuilder, CurrentTenantId(context), AnonymousFilter);
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

        return ApplyTenantFilters(modelBuilder, StaticTenantId(tenantContext), AnonymousFilter);
    }

#if NET10_0_OR_GREATER
    /// <summary>
    /// Applies the tenant filter of <see cref="UseTenantFilter{TContext}(ModelBuilder, TContext)"/> as a
    /// <em>named</em> query filter, so the model can carry further named filters next to it.
    /// </summary>
    /// <typeparam name="TContext">The context type; see <see cref="UseTenantFilter{TContext}(ModelBuilder, TContext)"/>.</typeparam>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="context">The context being built. Pass <c>this</c> from <c>OnModelCreating</c>.</param>
    /// <param name="filterName">The filter's name; defaults to <see cref="TenantFilterName"/>.</param>
    /// <returns>The model builder for chaining.</returns>
    /// <remarks>
    /// EF Core 10 refuses to combine an anonymous filter with named ones on one entity type
    /// (<c>AnonymousAndNamedFiltersCombined</c>), so a model that adds a soft-delete or any other filter
    /// by name applies the tenant filter through this method. A single query can then step around one
    /// filter alone, <c>IgnoreQueryFilters([TenantFilterName])</c>, and keep the others.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="modelBuilder"/> or <paramref name="context"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filterName"/> is null or blank.</exception>
    public static ModelBuilder UseNamedTenantFilter<TContext>(
        this ModelBuilder modelBuilder, TContext context, string filterName = TenantFilterName)
        where TContext : DbContext, ITenantScopedDbContext
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(filterName);

        return ApplyTenantFilters(modelBuilder, CurrentTenantId(context), (entity, filter) => entity.HasQueryFilter(filterName, filter));
    }

    /// <summary>
    /// Applies the tenant filter of <see cref="UseStaticTenantFilter"/> as a <em>named</em> query filter,
    /// so the model can carry further named filters next to it.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="tenantContext">The tenant context providing the current tenant identifier.</param>
    /// <param name="filterName">The filter's name; defaults to <see cref="TenantFilterName"/>.</param>
    /// <returns>The model builder for chaining.</returns>
    /// <remarks>
    /// The caveat of <see cref="UseStaticTenantFilter"/> applies unchanged: only for a process whose
    /// tenant never varies.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="modelBuilder"/> or <paramref name="tenantContext"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filterName"/> is null or blank.</exception>
    public static ModelBuilder UseNamedStaticTenantFilter(
        this ModelBuilder modelBuilder, ITenantContext tenantContext, string filterName = TenantFilterName)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(filterName);

        return ApplyTenantFilters(modelBuilder, StaticTenantId(tenantContext), (entity, filter) => entity.HasQueryFilter(filterName, filter));
    }
#endif

    // Typing the constant as the interface rather than as the concrete context type is what makes
    // EF Core replace it with the context executing the query: its rewriter matches any subtree
    // whose static type the context type is assignable to.
    private static MemberExpression CurrentTenantId(ITenantScopedDbContext context) =>
        Expression.Property(
            Expression.Constant(context, typeof(ITenantScopedDbContext)),
            nameof(ITenantScopedDbContext.CurrentTenantId));

    private static MemberExpression StaticTenantId(ITenantContext tenantContext) =>
        Expression.Property(Expression.Constant(tenantContext), nameof(ITenantContext.TenantId));

    private static void AnonymousFilter(EntityTypeBuilder entity, LambdaExpression filter) => entity.HasQueryFilter(filter);

    private static ModelBuilder ApplyTenantFilters(
        ModelBuilder modelBuilder, Expression currentTenantId, Action<EntityTypeBuilder, LambdaExpression> applyFilter)
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

            applyFilter(modelBuilder.Entity(entityType.ClrType), filter);
        }

        return modelBuilder;
    }
}
