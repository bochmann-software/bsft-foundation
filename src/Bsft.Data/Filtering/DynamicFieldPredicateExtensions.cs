using System.Linq.Expressions;
using System.Text.Json;

namespace Bsft.Data.Filtering;

/// <summary>
/// Builds predicates of a <see cref="DynamicField{TEntity}"/> from one another.
/// </summary>
public static class DynamicFieldPredicateExtensions
{
    /// <summary>
    /// Builds the <see cref="Contracts.Filtering.FilterOperator.In"/> predicate of a dynamic field as an
    /// <c>or</c> over its <see cref="Contracts.Filtering.FilterOperator.Eq"/> predicate, one per element.
    /// </summary>
    /// <typeparam name="TEntity">The entity the field belongs to.</typeparam>
    /// <param name="eq">The field's <c>eq</c> predicate builder.</param>
    /// <returns>An <c>in</c> predicate builder taking the JSON array of the set.</returns>
    /// <remarks>
    /// A field offers <c>in</c> only when its resolver names it, as a declared field does only when
    /// <c>AllowFilter</c> names it: this is the short way to name it for a field whose set membership
    /// means nothing more than equality with one of the values. A field that can test a set in one
    /// step (a jsonb containment, an array operator) supplies its own builder instead. The compiler
    /// checks the set before it calls the builder, so the array it receives holds at least one value
    /// and no <c>null</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="eq"/> is null.</exception>
    public static Func<JsonElement, Expression<Func<TEntity, bool>>> ToInPredicate<TEntity>(
        this Func<JsonElement, Expression<Func<TEntity, bool>>> eq)
    {
        ArgumentNullException.ThrowIfNull(eq);

        return set =>
        {
            if (set.ValueKind != JsonValueKind.Array || set.GetArrayLength() == 0)
                throw new ArgumentException("An 'in' predicate takes a non-empty JSON array.", nameof(set));

            var parameter = Expression.Parameter(typeof(TEntity), "x");
            var body = set.EnumerateArray()
                .Select(element => EntityMemberBinder.Bind(
                    eq(element) ?? throw new InvalidOperationException("The 'eq' predicate builder returned no predicate."),
                    parameter))
                .Aggregate(Expression.OrElse);
            return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
        };
    }
}
