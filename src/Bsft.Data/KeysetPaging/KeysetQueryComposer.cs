using System.Linq.Expressions;
using System.Reflection;
using Bsft.Data.Contracts.Pagination;
using Microsoft.EntityFrameworkCore.Query;

namespace Bsft.Data.KeysetPaging;

/// <summary>
/// Composes <c>WHERE (key &gt; @key) OR (key = @key AND id &gt; @id)</c> and <c>ORDER BY key, id</c> from
/// plain LINQ comparisons (see <see cref="IKeysetPageReader"/>).
/// </summary>
internal sealed class KeysetQueryComposer : IKeysetQueryComposer
{
    private static readonly MethodInfo StringCompare =
        typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;

    private static readonly ConstantExpression Zero = Expression.Constant(0);

    /// <inheritdoc />
    public IQueryable<TItem> Compose<TItem, TKey, TId>(
        IQueryable<TItem> query,
        KeysetOrder<TItem, TKey, TId> order,
        KeysetPosition<TKey, TId>? after)
    {
        var narrowed = after is null ? query : query.Where(BuildAfterPredicate(order, after));
        return order.Direction is SortDirection.Asc
            ? narrowed.OrderBy(order.Key).ThenBy(order.Id)
            : narrowed.OrderByDescending(order.Key).ThenByDescending(order.Id);
    }

    private static Expression<Func<TItem, bool>> BuildAfterPredicate<TItem, TKey, TId>(
        KeysetOrder<TItem, TKey, TId> order,
        KeysetPosition<TKey, TId> after)
    {
        var row = order.Key.Parameters[0];
        var key = order.Key.Body;
        var id = ReplacingExpressionVisitor.Replace(order.Id.Parameters[0], row, order.Id.Body);

        // Member access on a constant rather than the constant values themselves, so that EF Core turns
        // both into parameters (as it does a closure) instead of inlining them into the SQL text.
        var position = Expression.Constant(after);
        var afterKey = Expression.Property(position, nameof(KeysetPosition<TKey, TId>.Key));
        var afterId = Expression.Property(position, nameof(KeysetPosition<TKey, TId>.Id));

        var body = Expression.OrElse(
            BuildBeyondComparison(key, afterKey, order.Direction),
            Expression.AndAlso(Expression.Equal(key, afterKey), BuildBeyondComparison(id, afterId, order.Direction)));
        return Expression.Lambda<Func<TItem, bool>>(body, row);
    }

    /// <summary>The comparison that holds for a column value later in the order than the position's.</summary>
    private static BinaryExpression BuildBeyondComparison(Expression column, Expression value, SortDirection direction)
    {
        // A string has no ordering operator; string.Compare(a, b) > 0 is what EF Core rewrites to a > b.
        var (left, right) = column.Type == typeof(string)
            ? (Expression.Call(StringCompare, column, value), Zero)
            : (column, value);
        return direction is SortDirection.Asc
            ? Expression.GreaterThan(left, right)
            : Expression.LessThan(left, right);
    }
}
