using System.Linq.Expressions;
using System.Reflection;
using Bsft.Data.Contracts.Search;

namespace Bsft.Data.Search;

/// <summary>
/// Builds the projection of a row onto its ranked hit.
/// </summary>
/// <remarks>
/// Shared by the three provider packages, which differ only in how they compute the score. Internal,
/// so it is not a surface an application reaches.
/// </remarks>
internal static class RankedItemProjection
{
    /// <summary>
    /// Binds an entity and its score onto a <see cref="RankedItem{TEntity}"/>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="item">The expression reading the entity.</param>
    /// <param name="score">The expression computing the relevance score.</param>
    /// <returns>The projection body, which a caller wraps in the lambda shape its query needs.</returns>
    /// <remarks>
    /// A member-initialized projection, not a constructor call: Entity Framework Core can only push an
    /// <c>ORDER BY</c> on a projected member down into SQL for the former.
    /// </remarks>
    public static MemberInitExpression For<TEntity>(Expression item, Expression score) =>
        Expression.MemberInit(
            Expression.New(typeof(RankedItem<TEntity>)),
            Expression.Bind(RankedItemMembers<TEntity>.Item, item),
            Expression.Bind(RankedItemMembers<TEntity>.Score, score));

    /// <summary>
    /// The two properties bound above, resolved once per closed entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <remarks>
    /// The lookup answers the same pair for the life of the process, and a query is built per request:
    /// a static generic holder is what turns that into one reflection call per entity type rather than
    /// two per search.
    /// </remarks>
    private static class RankedItemMembers<TEntity>
    {
        public static readonly PropertyInfo Item =
            typeof(RankedItem<TEntity>).GetProperty(nameof(RankedItem<TEntity>.Item))!;

        public static readonly PropertyInfo Score =
            typeof(RankedItem<TEntity>).GetProperty(nameof(RankedItem<TEntity>.Score))!;
    }
}
