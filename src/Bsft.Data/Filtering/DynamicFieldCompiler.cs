using Bsft.Data.Contracts.Filtering;
using System.Linq.Expressions;
using System.Text.Json;

namespace Bsft.Data.Filtering;

/// <summary>
/// Compiles a comparison or presence test on a dynamic field (<see cref="DynamicField{TEntity}"/>).
/// </summary>
/// <remarks>
/// The resolver supplies the presence test and the positive predicates; the missing-value semantics are
/// composed here, the same for every resolver: <c>eq null</c> is NOT present, <c>neq null</c> is present,
/// a positive operator is <c>present AND p</c>, a negated one <c>NOT present OR NOT p</c>, and <c>in</c>
/// is <c>present AND</c> the resolver's own <c>in</c> predicate.
/// </remarks>
internal static class DynamicFieldCompiler
{
    internal static Expression Present<T>(DynamicField<T> field, ParameterExpression parameter) =>
        Inline(field.Present, parameter);

    internal static Expression Compare<T>(ComparisonNode node, DynamicField<T> field, ParameterExpression parameter)
    {
        var present = Inline(field.Present, parameter);

        if (node.Op == FilterOperator.In)
            return Expression.AndAlso(present, BuildIn(node, field, parameter));

        if (node.Value.ValueKind == JsonValueKind.Null)
        {
            return node.Op switch
            {
                FilterOperator.Eq => Expression.Not(present),
                FilterOperator.Neq => present,
                _ => throw FilterErrors.NullWithNonEqualityOperator(node.Op, node.Field),
            };
        }

        var (positive, negated) = ResolvePositive(node.Op);
        if (!field.Predicates.TryGetValue(positive, out var builder))
            throw FilterErrors.OperatorNotAllowed(node.Op, node.Field);

        var predicate = Inline(builder(node.Value), parameter);
        return negated
            ? Expression.OrElse(Expression.Not(present), Expression.Not(predicate))
            : Expression.AndAlso(present, predicate);
    }

    // `in` is offered only when the resolver names it, the way a declared field offers it only when
    // AllowFilter does; a resolver whose `in` is an `or` over `eq` names it with ToInPredicate.
    private static Expression BuildIn<T>(ComparisonNode node, DynamicField<T> field, ParameterExpression parameter)
    {
        node.Read();

        if (!field.Predicates.TryGetValue(FilterOperator.In, out var inBuilder))
            throw FilterErrors.OperatorNotAllowed(node.Op, node.Field);

        return Inline(inBuilder(node.Value), parameter);
    }

    private static (FilterOperator Positive, bool Negated) ResolvePositive(FilterOperator op) => op switch
    {
        FilterOperator.Neq => (FilterOperator.Eq, true),
        FilterOperator.NotContains => (FilterOperator.Contains, true),
        FilterOperator.NotStartsWith => (FilterOperator.StartsWith, true),
        FilterOperator.NotEndsWith => (FilterOperator.EndsWith, true),
        _ => (op, false),
    };

    // Rebinds a resolver's predicate onto the compiled predicate's parameter. A builder that returns
    // no expression is a broken resolver, not a bad request.
    private static Expression Inline<T>(Expression<Func<T, bool>>? predicate, ParameterExpression parameter) =>
        EntityMemberBinder.Bind(
            predicate ?? throw new InvalidOperationException("A dynamic field resolver returned no predicate."),
            parameter);
}
