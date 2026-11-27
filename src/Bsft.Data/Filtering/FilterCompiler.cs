using Bsft.Data.Contracts.Filtering;
using System.Linq.Expressions;
using System.Text.Json;

namespace Bsft.Data.Filtering;

/// <summary>
/// Compiles a <see cref="FilterNode"/> AST into a LINQ expression tree.
/// </summary>
public static class FilterCompiler
{
    /// <summary>
    /// Compiles the given filter node into a strongly-typed predicate expression.
    /// </summary>
    /// <typeparam name="T">The entity type to filter.</typeparam>
    /// <param name="node">The filter AST root node.</param>
    /// <param name="fieldMap">The query field map that resolves field names to entity expressions.</param>
    /// <param name="stringMatch">
    /// How string comparisons are made case-insensitive. Defaults to <see cref="FilterStringMatch.LowerInvariant"/>,
    /// which is portable but non-sargable; see <see cref="FilterStringMatch"/> for the trade-off.
    /// </param>
    /// <returns>A compiled expression that can be used in LINQ queries.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node"/> or <paramref name="fieldMap"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when an unknown node type is encountered.</exception>
    public static Expression<Func<T, bool>> Compile<T>(
        this FilterNode node,
        QueryFieldMap<T> fieldMap,
        FilterStringMatch stringMatch = FilterStringMatch.LowerInvariant)
    {
        if (node is null)
            throw new ArgumentNullException(nameof(node));
        if (fieldMap is null)
            throw new ArgumentNullException(nameof(fieldMap));

        // The size of the in sets is checked over the whole tree first: the total is a property of the
        // filter, not of one node, and nothing is built for a filter that is refused anyway.
        FilterInValues.EnsureWithinLimits(node, fieldMap.MaxInValuesPerSet, fieldMap.MaxInValuesPerFilter);

        var parameter = Expression.Parameter(typeof(T), "x");
        var body = BuildExpression(node, parameter, fieldMap, stringMatch);
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static Expression BuildExpression<T>(FilterNode node, ParameterExpression parameter, QueryFieldMap<T> fieldMap, FilterStringMatch stringMatch)
    {
        return node switch
        {
            ComparisonNode comparison => BuildComparison(comparison, parameter, fieldMap, stringMatch),
            PresenceNode presence => BuildPresence(presence, parameter, fieldMap),
            AndNode andNode => BuildAnd(andNode, parameter, fieldMap, stringMatch),
            OrNode orNode => BuildOr(orNode, parameter, fieldMap, stringMatch),
            _ => throw new InvalidOperationException($"Unknown filter node type: {node.GetType().Name}")
        };
    }

    private static Expression BuildComparison<T>(ComparisonNode node, ParameterExpression parameter, QueryFieldMap<T> fieldMap, FilterStringMatch stringMatch)
    {
        // Presence is unary and has its own `present` node: a `comparison` carrying the `pr` operator
        // (which compares against a value it does not have) is a malformed request. Reject it as a 400
        // here rather than let it fall through to the value-comparison switch as an unhandled 500.
        if (node.Op == FilterOperator.Present)
            throw FilterErrors.PresenceAsComparison(node.Field);

        if (fieldMap.TryResolveDynamic(node.Field, out var dynamicField))
            return DynamicFieldCompiler.Compare(node, dynamicField ?? throw FilterErrors.UnknownField(node.Field), parameter);

        var mapping = fieldMap.GetField(node.Field, node.Op);
        var memberExpression = EntityMemberBinder.Bind(mapping.EntityExpression, parameter);

        if (node.Op == FilterOperator.In)
            return FilterSetCompiler.In(node, memberExpression, stringMatch);

        // A string operator against a non-string field is a client mistake, not an internal fault:
        // reject it as a ValidationException (400 keyed on `filter`) before the expression builder is
        // asked to coalesce a non-string member into a string, which would throw as an unhandled 500.
        if (FilterStringCompiler.IsStringOperation(node.Op) && memberExpression.Type != typeof(string))
            throw FilterErrors.StringOperatorOnNonStringField(node.Op, node.Field);

        var isNullValue = node.Value.ValueKind == JsonValueKind.Null;

        // A null filter value only fits a member that can hold one. Asked to read a JSON null as the
        // unwrapped type, System.Text.Json throws a JsonException nothing here catches, and the client
        // gets a 500 for what is a bad request; a member that cannot be null has no null to compare
        // with at all. So the nullable type is kept for the comparison, and a non-nullable member is
        // refused the same way every other client mistake in this method is.
        if (isNullValue && memberExpression.Type.IsValueType
            && Nullable.GetUnderlyingType(memberExpression.Type) is null)
        {
            throw FilterErrors.NullValueOnNonNullableField(node.Field);
        }

        // Null is not a value to compare against, it is the absence of one, so only equality says
        // anything about it. Everything else would compile to an answer that is wrong rather than
        // absent: `co` against a null value lowers to Contains("") and matches every row, and an
        // ordering operator lifts to a comparison that is false for every row including the null ones.
        if (isNullValue && node.Op is not (FilterOperator.Eq or FilterOperator.Neq))
            throw FilterErrors.NullWithNonEqualityOperator(node.Op, node.Field);

        // Compare against the member's own null rather than going through the value path. The value
        // path exists to parameterize a value and, in LowerInvariant mode, to fold both operands to
        // lower case, which coalesces a null column to "" and would make `eq null` match every empty
        // string as well. `col IS NULL` is what the request asks for and what EF Core emits from this.
        if (isNullValue)
        {
            var nullConstant = Expression.Constant(null, memberExpression.Type);
            return node.Op == FilterOperator.Eq
                ? Expression.Equal(memberExpression, nullConstant)
                : Expression.NotEqual(memberExpression, nullConstant);
        }

        // The value takes the member's own type, Nullable<T> included: a comparison needs both operands
        // of one type, and lifting only the member left `int? == int`, which Expression refuses.
        var value = DeserializeValue(node.Value, memberExpression.Type);
        var valueExpression = FilterOperands.Parameterize(value, memberExpression.Type);

        if (FilterStringCompiler.IsStringOperation(node.Op))
            return FilterStringCompiler.Compare(node.Op, memberExpression, valueExpression, stringMatch);

        return BuildValueComparison(node.Op, memberExpression, valueExpression, stringMatch);
    }

    private static Expression BuildPresence<T>(PresenceNode node, ParameterExpression parameter, QueryFieldMap<T> fieldMap)
    {
        // Presence runs through the same per-field whitelist as every other operator: a field must
        // declare AllowFilter(FilterOperator.Present) for a presence test to resolve at all.
        if (fieldMap.TryResolveDynamic(node.Field, out var dynamicField))
            return DynamicFieldCompiler.Present(dynamicField ?? throw FilterErrors.UnknownField(node.Field), parameter);

        var mapping = fieldMap.GetField(node.Field, FilterOperator.Present);
        var memberExpression = EntityMemberBinder.Bind(mapping.EntityExpression, parameter);

        // A non-nullable value type always holds a value, so presence is a constant truth. Anything
        // that can be null — a reference type or a Nullable<T> — is present exactly when it is not
        // null, which translates to `col IS NOT NULL` under EF Core: a bound, parameter-free predicate.
        if (memberExpression.Type.IsValueType && Nullable.GetUnderlyingType(memberExpression.Type) is null)
            return Expression.Constant(true);

        return Expression.NotEqual(memberExpression, Expression.Constant(null, memberExpression.Type));
    }

    private static Expression BuildValueComparison(FilterOperator op, Expression member, Expression value, FilterStringMatch stringMatch)
    {
        if (member.Type == typeof(string) && stringMatch == FilterStringMatch.LowerInvariant)
        {
            member = FilterOperands.ToLowerNullSafe(member);
            value = FilterOperands.ToLowerNullSafe(value);
        }

        return op switch
        {
            FilterOperator.Eq => Expression.Equal(member, value),
            FilterOperator.Neq => Expression.NotEqual(member, value),
            FilterOperator.Gt => Expression.GreaterThan(member, value),
            FilterOperator.Gte => Expression.GreaterThanOrEqual(member, value),
            FilterOperator.Lt => Expression.LessThan(member, value),
            FilterOperator.Lte => Expression.LessThanOrEqual(member, value),
            _ => throw new InvalidOperationException($"Unsupported value operator: {op}")
        };
    }

    private static Expression BuildAnd<T>(AndNode node, ParameterExpression parameter, QueryFieldMap<T> fieldMap, FilterStringMatch stringMatch)
    {
        if (node.And.Count == 0)
            return Expression.Constant(true);

        var expressions = node.And.Select(child => BuildExpression(child, parameter, fieldMap, stringMatch));
        return expressions.Aggregate(Expression.AndAlso);
    }

    private static Expression BuildOr<T>(OrNode node, ParameterExpression parameter, QueryFieldMap<T> fieldMap, FilterStringMatch stringMatch)
    {
        if (node.Or.Count == 0)
            return Expression.Constant(false);

        var expressions = node.Or.Select(child => BuildExpression(child, parameter, fieldMap, stringMatch));
        return expressions.Aggregate(Expression.OrElse);
    }

    private static object? DeserializeValue(JsonElement element, Type targetType)
    {
        return element.Deserialize(targetType);
    }
}
