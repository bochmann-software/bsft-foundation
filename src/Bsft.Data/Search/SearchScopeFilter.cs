using System.Linq.Expressions;
using Bsft.Data.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.Search;

/// <summary>
/// Applies the visibility boundary to a queryable before the search provider ever sees it.
/// </summary>
/// <remarks>
/// Scoping is enforced here, in the pipeline, and not left to the caller. A search endpoint that
/// forgets its org filter leaks personnel data across branches; that must not be possible by
/// omission.
/// </remarks>
internal static class SearchScopeFilter
{
    /// <summary>
    /// Answers which boundaries an entity can be scoped by. Both the startup validator and this
    /// filter ask here, so a future scoping dimension cannot be wired into one and forgotten in the
    /// other — which would leave the validator certifying a group the runtime does not actually limit.
    /// </summary>
    /// <param name="entityType">The entity type to inspect.</param>
    /// <returns><see langword="true"/> when the entity carries the tenant marker.</returns>
    public static bool IsTenantScoped(Type entityType) => typeof(ITenantEntity).IsAssignableFrom(entityType);

    /// <inheritdoc cref="IsTenantScoped"/>
    public static bool IsOrganizationScoped(Type entityType) => typeof(IOrganizationScopedEntity).IsAssignableFrom(entityType);

    /// <summary>Whether the entity can be scoped at all.</summary>
    /// <param name="entityType">The entity type to inspect.</param>
    /// <returns><see langword="true"/> when the entity carries any scoping marker.</returns>
    public static bool IsScopable(Type entityType) => IsTenantScoped(entityType) || IsOrganizationScoped(entityType);

    /// <summary>
    /// Whether an <see cref="ISearchScopeProvider"/> is registered. Asked identically by the startup
    /// validator and by the executor, so it lives in one place.
    /// </summary>
    /// <param name="scopeFactory">Factory used to open a scope the provider can be resolved from.</param>
    /// <returns><see langword="true"/> when a provider is registered.</returns>
    public static bool HasScopeProvider(IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetService<ISearchScopeProvider>() is not null;
    }

    /// <summary>
    /// Narrows a queryable to what the caller may see, before the search provider ever touches it.
    /// </summary>
    /// <typeparam name="TEntity">The entity being searched.</typeparam>
    /// <param name="query">The source queryable.</param>
    /// <param name="scope">The resolved visibility boundary.</param>
    /// <param name="group">The search group, which decides whether a boundary is required at all.</param>
    /// <returns>The scoped queryable.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a scoped group's entity carries a marker the resolved scope leaves unset.
    /// </exception>
    public static IQueryable<TEntity> Apply<TEntity>(IQueryable<TEntity> query, SearchScopeState scope, SearchGroupConfiguration group)
    {
        // The answer is constant for a closed TEntity, so it is computed once per type rather than
        // once per search request.
        var isTenantScoped = ScopeMarkers<TEntity>.IsTenantScoped;
        var isOrgScoped = ScopeMarkers<TEntity>.IsOrganizationScoped;

        // A null field means "no restriction". That is a legitimate answer for a group declared
        // Unscoped(), and a hole everywhere else: a scope provider that forgets a dimension the
        // entity actually carries would otherwise return every branch's rows without a trace.
        //
        // Note what IsUnscoped does *not* do below: it never suppresses the boundary itself. Startup
        // validation only accepts Unscoped() on an entity carrying no scoping marker, so for every
        // declaration that survives validation both markers are false and the two Where clauses are
        // unreachable anyway — the group is boundary-free by construction rather than by a flag. If
        // a refused declaration reaches here regardless, the boundary stays on, which is the safe
        // direction: the flag can narrow nothing and can widen nothing.
        if (!group.IsUnscoped)
        {
            if (isTenantScoped && scope.TenantId is null)
                throw MissingDimension(group, nameof(ITenantEntity), nameof(SearchScopeState.TenantId));

            if (isOrgScoped && scope.AllowedOrganizationUnitIds is null)
                throw MissingDimension(group, nameof(IOrganizationScopedEntity), nameof(SearchScopeState.AllowedOrganizationUnitIds));
        }

        if (scope.TenantId is not null && isTenantScoped)
            query = query.Where(TenantPredicate<TEntity>(scope.TenantId));

        if (scope.AllowedOrganizationUnitIds is not null && isOrgScoped)
            query = query.Where(OrganizationUnitPredicate<TEntity>(scope.AllowedOrganizationUnitIds));

        return query;
    }

    private static InvalidOperationException MissingDimension(SearchGroupConfiguration group, string marker, string field) =>
        new($"Search group '{group.Name}' is on an entity implementing {marker}, but the resolved {nameof(SearchScopeState)} left {field} unset, which would search across every one of them. Set it. Unscoped() is refused on an entity that carries a scoping marker, so it is not the way out of this one.");

    private static class ScopeMarkers<TEntity>
    {
        public static readonly bool IsTenantScoped = SearchScopeFilter.IsTenantScoped(typeof(TEntity));

        public static readonly bool IsOrganizationScoped = SearchScopeFilter.IsOrganizationScoped(typeof(TEntity));
    }

    private static Expression<Func<TEntity, bool>> TenantPredicate<TEntity>(string tenantId)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var property = Expression.Property(parameter, nameof(ITenantEntity.TenantId));
        var body = Expression.Equal(property, Expression.Constant(tenantId, typeof(string)));
        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    private static Expression<Func<TEntity, bool>> OrganizationUnitPredicate<TEntity>(IReadOnlyList<Guid> allowed)
    {
        var ids = allowed.ToList();
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var property = Expression.Property(parameter, nameof(IOrganizationScopedEntity.OrganizationUnitId));

        var contains = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(Guid)],
            Expression.Constant(ids),
            property);

        return Expression.Lambda<Func<TEntity, bool>>(contains, parameter);
    }
}
