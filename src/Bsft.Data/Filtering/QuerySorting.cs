using System.Linq.Expressions;
using System.Reflection;
using Bsft.Data.Contracts.Pagination;


namespace Bsft.Data.Filtering;

/// <summary>
/// Composes dynamic <c>OrderBy</c>/<c>ThenBy</c> chains from sort criteria. Shared by the query
/// executor and the search executor so both order results the same way.
/// </summary>
internal static class QuerySorting
{
    /// <summary>
    /// Applies sort criteria to a queryable in the order they were given.
    /// </summary>
    /// <typeparam name="TEntity">The entity being sorted.</typeparam>
    /// <param name="query">The source queryable.</param>
    /// <param name="sortItems">The sort criteria, most significant first.</param>
    /// <param name="fieldMap">The compiled field map that resolves a DTO field to an entity expression.</param>
    /// <returns>The ordered queryable, or the source when no criteria were given.</returns>
    /// <remarks>
    /// The entity's unique key is appended as a final <c>ThenBy</c> unless the criteria already order
    /// by it. Without it a sort on a non-unique column leaves rows with the same value in whatever
    /// order the provider picks, and <c>Skip</c>/<c>Take</c> can then hand the same row to two pages
    /// and another to none — the same reason every store in the framework writes
    /// <c>ThenBy(e =&gt; e.Id)</c> by hand.
    /// </remarks>
    public static IQueryable<TEntity> Apply<TEntity>(
        IQueryable<TEntity> query,
        IReadOnlyList<SortItem> sortItems,
        QueryFieldMap<TEntity> fieldMap)
    {
        IOrderedQueryable<TEntity>? ordered = null;

        // The resolved selector of each item is captured here so the tie-breaker check does not have
        // to resolve the same field a second time.
        var selectors = new List<LambdaExpression>(sortItems.Count);

        for (var i = 0; i < sortItems.Count; i++)
        {
            var item = sortItems[i];
            var expression = fieldMap.GetSortField(item.Field);
            selectors.Add(expression);
            var parameter = Expression.Parameter(typeof(TEntity), "x");
            var body = Rebind(expression.Body, expression.Parameters[0], parameter);
            var keySelector = Expression.Lambda(body, parameter);

            var methodName = i == 0
                ? item.Direction is SortDirection.Asc ? "OrderBy" : "OrderByDescending"
                : item.Direction is SortDirection.Asc ? "ThenBy" : "ThenByDescending";

            var call = Expression.Call(
                typeof(Queryable),
                methodName,
                [typeof(TEntity), body.Type],
                (ordered ?? query).Expression,
                Expression.Quote(keySelector));

            ordered = (IOrderedQueryable<TEntity>)query.Provider.CreateQuery<TEntity>(call);
        }

        return ordered is null ? query : AppendKeyTieBreaker(ordered, selectors);
    }

    /// <summary>
    /// Orders a queryable by a single selector written against the entity. The last resort when no
    /// criteria establish an order at all.
    /// </summary>
    /// <typeparam name="TEntity">The entity being sorted.</typeparam>
    /// <param name="query">The source queryable.</param>
    /// <param name="selector">The key selector to order by.</param>
    /// <returns>The ordered queryable.</returns>
    public static IQueryable<TEntity> OrderBy<TEntity>(IQueryable<TEntity> query, LambdaExpression selector) =>
        Chain(query, selector, nameof(Queryable.OrderBy));

    /// <summary>
    /// Resolves the last-resort ordering when no sort criteria establish one: the entity's own unique
    /// key when it follows the convention, and otherwise the caller's first sortable field. Both the
    /// query and the search executor resolve their fallback here, so a key-bearing entity pages by a
    /// unique tie-breaker on either path.
    /// </summary>
    /// <typeparam name="TEntity">The entity being ordered.</typeparam>
    /// <param name="firstSortableField">
    /// The selector to fall back to when the entity follows no key convention, or
    /// <see langword="null"/> when the caller has none either.
    /// </param>
    /// <returns>The key selector, the fallback, or <see langword="null"/> when neither exists.</returns>
    public static LambdaExpression? ResolveTieBreaker<TEntity>(LambdaExpression? firstSortableField) =>
        EntityKeyResolver.Resolve(typeof(TEntity))?.Selector ?? firstSortableField;

    /// <summary>
    /// Resolves the ordered secondary sort steps applied after the score on the ranked search path.
    /// A configured default sort comes first, so the host's intent for the secondary order is honored,
    /// then the entity's own unique key as the final tie-breaker, so paging stays deterministic even
    /// when the default sort is non-unique. With no default sort the key stands alone; for an entity
    /// that follows no key convention the caller's first sortable field is the last resort.
    /// </summary>
    /// <typeparam name="TEntity">The entity being ordered.</typeparam>
    /// <param name="defaultSort">
    /// The configured default sort selector, or <see langword="null"/> when none is configured.
    /// </param>
    /// <param name="defaultSortDirection">The direction the configured default sort is applied in.</param>
    /// <param name="firstSortableField">
    /// The selector to fall back to when the entity follows no key convention and no default sort is
    /// configured.
    /// </param>
    /// <returns>The ordered secondary sort steps, most significant first.</returns>
    public static IReadOnlyList<RankTieBreaker> ResolveRankTieBreakers<TEntity>(
        LambdaExpression? defaultSort,
        SortDirection defaultSortDirection,
        LambdaExpression firstSortableField)
    {
        if (defaultSort is not null)
        {
            var steps = new List<RankTieBreaker>(2) { new(defaultSort, defaultSortDirection) };

            // The key is the final unique tie-breaker beneath the default sort, unless the default sort
            // already orders by it — appending it again would only add a redundant, and after a
            // descending default sort on the key contradictory, ordering. A keyless entity leans on the
            // default sort alone.
            if (EntityKeyResolver.Resolve(typeof(TEntity)) is { } key && !IsKeySelector(defaultSort, key.Member))
                steps.Add(new RankTieBreaker(key.Selector, SortDirection.Asc));

            return steps;
        }

        // No default sort: the same last-resort tie-breaker the query path uses — the entity key, or
        // the first sortable field for a keyless entity. The field is always present on the search
        // path, so the resolved selector is never null here.
        return [new RankTieBreaker(ResolveTieBreaker<TEntity>(firstSortableField)!, SortDirection.Asc)];
    }

    private static bool IsKeySelector(LambdaExpression? selector, MemberInfo keyMember) =>
        selector is not null
        && EntityKeyResolver.FindMember(selector.Body) is { } member
        && IsSameMember(member, keyMember);

    private static IQueryable<TEntity> AppendKeyTieBreaker<TEntity>(
        IOrderedQueryable<TEntity> ordered,
        List<LambdaExpression> selectors)
    {
        if (EntityKeyResolver.Resolve(typeof(TEntity)) is not { } key || ContainsSortOnKey(selectors, key.Member))
            return ordered;

        return Chain(ordered, key.Selector, nameof(Queryable.ThenBy));
    }

    /// <summary>
    /// Tells whether the criteria already sort on the key, in which case appending it again would
    /// only add a redundant — and, after a descending sort on it, contradictory — ordering.
    /// </summary>
    private static bool ContainsSortOnKey(List<LambdaExpression> selectors, MemberInfo keyMember)
    {
        foreach (var selector in selectors)
        {
            var member = EntityKeyResolver.FindMember(selector.Body);
            if (member is not null && IsSameMember(member, keyMember))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Tells whether two members are the same key, by identity rather than by name alone. A field
    /// mapped through a navigation resolves to the related type's own key member, whose name can match
    /// the entity's key while belonging to another type — comparing the name alone would then suppress
    /// the tie-breaker the entity still needs.
    /// </summary>
    private static bool IsSameMember(MemberInfo member, MemberInfo keyMember) =>
        member == keyMember
        || (member.DeclaringType == keyMember.DeclaringType && string.Equals(member.Name, keyMember.Name, StringComparison.Ordinal));

    private static IQueryable<TEntity> Chain<TEntity>(IQueryable<TEntity> query, LambdaExpression selector, string methodName)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var body = Rebind(selector.Body, selector.Parameters[0], parameter);

        var call = Expression.Call(
            typeof(Queryable),
            methodName,
            [typeof(TEntity), body.Type],
            query.Expression,
            Expression.Quote(Expression.Lambda(body, parameter)));

        return query.Provider.CreateQuery<TEntity>(call);
    }

    /// <summary>
    /// Replaces a lambda parameter with an arbitrary expression, so a selector written against the
    /// entity can be reused on a wrapper such as a ranked hit.
    /// </summary>
    /// <param name="body">The expression body to rewrite.</param>
    /// <param name="from">The lambda parameter to replace.</param>
    /// <param name="to">The expression to put in its place.</param>
    /// <returns>The rewritten expression.</returns>
    public static Expression Rebind(Expression body, ParameterExpression from, Expression to) =>
        new ParameterRebinder(from, to).Visit(body);

    private sealed class ParameterRebinder : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly Expression _to;

        public ParameterRebinder(ParameterExpression from, Expression to)
        {
            _from = from;
            _to = to;
        }

        /// <inheritdoc/>
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? _to : base.VisitParameter(node);
    }
}
