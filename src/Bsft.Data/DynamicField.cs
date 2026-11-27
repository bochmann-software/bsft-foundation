using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Text.Json;
using Bsft.Data.Contracts.Filtering;

namespace Bsft.Data;

/// <summary>
/// What one dynamic field allows a filter and a sort to do, as an <see cref="IDynamicFieldResolver{TEntity}"/>
/// answers it for one request.
/// </summary>
/// <typeparam name="TEntity">The entity the field belongs to.</typeparam>
/// <remarks>
/// <para>
/// The resolver supplies only positive predicates; the compiler composes everything else from them, so
/// the meaning of a missing value is the same for every resolver and cannot be got wrong in one of
/// them:
/// </para>
/// <list type="bullet">
/// <item><description><c>pr</c> is <see cref="Present"/>, and <c>{field, neq, null}</c> means the same.</description></item>
/// <item><description><c>{field, eq, null}</c> is the canonical form of "not set": <c>NOT present</c>.</description></item>
/// <item><description>A positive operator (<c>eq</c>, <c>gt</c>, <c>contains</c>, <c>in</c>, ...) is
/// <c>present AND predicate</c>, so a missing field never matches one.</description></item>
/// <item><description>A negated operator (<c>neq</c>, <c>!contains</c>, ...) is
/// <c>NOT present OR NOT predicate</c>, so a missing field always matches one.</description></item>
/// <item><description><c>in</c> is offered only when <see cref="FilterOperator.In"/> is a key, as a
/// declared field offers it only when <c>AllowFilter</c> names it. A field whose set membership is an
/// <c>or</c> over <c>eq</c> names it with
/// <c>Filtering.DynamicFieldPredicateExtensions.ToInPredicate</c>.</description></item>
/// </list>
/// <para>
/// A predicate is therefore only ever asked about a row on which <see cref="Present"/> holds, and in
/// SQL a predicate that yields <c>NULL</c> on a missing key cannot leak into a negation. What
/// <see cref="Present"/> must answer for a stored JSON <c>null</c> is the resolver's decision; a
/// resolver that treats JSON <c>null</c> as "not set" answers <see langword="false"/> for it.
/// </para>
/// </remarks>
public sealed class DynamicField<TEntity>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicField{TEntity}"/> class.
    /// </summary>
    /// <param name="present">The presence test: whether the row holds a value for the field.</param>
    /// <param name="predicates">
    /// One predicate builder per positive operator the field allows, taking the filter value. The
    /// operators <see cref="FilterOperator.Eq"/>, <see cref="FilterOperator.Gt"/>,
    /// <see cref="FilterOperator.Gte"/>, <see cref="FilterOperator.Lt"/>, <see cref="FilterOperator.Lte"/>,
    /// <see cref="FilterOperator.Contains"/>, <see cref="FilterOperator.StartsWith"/>,
    /// <see cref="FilterOperator.EndsWith"/> and <see cref="FilterOperator.In"/> may be keys; a negated
    /// operator is allowed exactly when its positive counterpart is a key; <c>in</c> only when it is a key
    /// itself. A builder throws a <see cref="Bsft.Core.ValidationException"/> for a value of a
    /// kind the field cannot compare with.
    /// </param>
    /// <param name="sort">
    /// The sort key, a lambda over <typeparamref name="TEntity"/>, or <see langword="null"/> when the
    /// field may not be sorted on.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="present"/> or <paramref name="predicates"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a key is <see cref="FilterOperator.Present"/> or a negated operator, a builder is
    /// null, or <paramref name="sort"/> does not take exactly one <typeparamref name="TEntity"/>.
    /// </exception>
    public DynamicField(
        Expression<Func<TEntity, bool>> present,
        IReadOnlyDictionary<FilterOperator, Func<JsonElement, Expression<Func<TEntity, bool>>>> predicates,
        LambdaExpression? sort = null)
    {
        Present = present ?? throw new ArgumentNullException(nameof(present));
        if (predicates is null)
            throw new ArgumentNullException(nameof(predicates));

        var copy = new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<TEntity, bool>>>>(predicates.Count);
        foreach (var (op, builder) in predicates)
        {
            if (!IsPositive(op))
            {
                throw new ArgumentException(
                    $"'{op}' cannot carry a predicate of its own: presence is the 'present' argument and every negation is composed from its positive operator.",
                    nameof(predicates));
            }

            copy[op] = builder ?? throw new ArgumentException($"The predicate builder for '{op}' is null.", nameof(predicates));
        }

        if (sort is not null && (sort.Parameters.Count != 1 || !sort.Parameters[0].Type.IsAssignableFrom(typeof(TEntity))))
            throw new ArgumentException($"The sort key must be a lambda over a single {typeof(TEntity).Name}.", nameof(sort));

        Predicates = new ReadOnlyDictionary<FilterOperator, Func<JsonElement, Expression<Func<TEntity, bool>>>>(copy);
        Sort = sort;
    }

    /// <summary>Gets the presence test.</summary>
    public Expression<Func<TEntity, bool>> Present { get; }

    /// <summary>Gets the predicate builders, one per positive operator the field allows.</summary>
    public IReadOnlyDictionary<FilterOperator, Func<JsonElement, Expression<Func<TEntity, bool>>>> Predicates { get; }

    /// <summary>Gets the sort key, or <see langword="null"/> when the field may not be sorted on.</summary>
    public LambdaExpression? Sort { get; }

    /// <summary>Whether an operator is one a resolver supplies a predicate for.</summary>
    /// <param name="op">The operator.</param>
    /// <returns><see langword="true"/> for the positive comparison operators and <c>in</c>.</returns>
    internal static bool IsPositive(FilterOperator op) =>
        op is FilterOperator.Eq or FilterOperator.Gt or FilterOperator.Gte or FilterOperator.Lt
            or FilterOperator.Lte or FilterOperator.Contains or FilterOperator.StartsWith
            or FilterOperator.EndsWith or FilterOperator.In;
}
