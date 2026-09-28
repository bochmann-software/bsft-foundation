using Bsft.Core;
using Bsft.Data.Contracts.Filtering;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace Bsft.Data.Filtering;

/// <summary>
/// Compiles a <see cref="FilterNode"/> AST into a LINQ expression tree.
/// </summary>
public static class FilterCompiler
{
    private static readonly MethodInfo ToLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes)!;

    private static readonly PropertyInfo ValueProperty = typeof(ValueCarrier).GetProperty(nameof(ValueCarrier.Value))!;

    private static readonly ConstantExpression NullString = Expression.Constant(null, typeof(string));

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
            throw PresenceAsComparison(node.Field);

        var mapping = fieldMap.GetField(node.Field, node.Op);
        var memberExpression = EntityMemberBinder.Bind(mapping.EntityExpression, parameter);

        // A string operator against a non-string field is a client mistake, not an internal fault:
        // reject it as a ValidationException (400 keyed on `filter`) before the expression builder is
        // asked to coalesce a non-string member into a string, which would throw as an unhandled 500.
        if (IsStringOperation(node.Op) && memberExpression.Type != typeof(string))
            throw StringOperatorOnNonStringField(node.Op, node.Field);

        var isNullValue = node.Value.ValueKind == JsonValueKind.Null;

        // A null filter value only fits a member that can hold one. Asked to read a JSON null as the
        // unwrapped type, System.Text.Json throws a JsonException nothing here catches, and the client
        // gets a 500 for what is a bad request; a member that cannot be null has no null to compare
        // with at all. So the nullable type is kept for the comparison, and a non-nullable member is
        // refused the same way every other client mistake in this method is.
        if (isNullValue && memberExpression.Type.IsValueType
            && Nullable.GetUnderlyingType(memberExpression.Type) is null)
        {
            throw NullValueOnNonNullableField(node.Field);
        }

        // Null is not a value to compare against, it is the absence of one, so only equality says
        // anything about it. Everything else would compile to an answer that is wrong rather than
        // absent: `co` against a null value lowers to Contains("") and matches every row, and an
        // ordering operator lifts to a comparison that is false for every row including the null ones.
        if (isNullValue && node.Op is not (FilterOperator.Eq or FilterOperator.Neq))
            throw NullWithNonEqualityOperator(node.Op, node.Field);

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
        var valueExpression = BuildValueAccessor(value, memberExpression.Type);

        if (IsStringOperation(node.Op))
            return BuildStringComparison(node.Op, memberExpression, valueExpression, stringMatch);

        return BuildValueComparison(node.Op, memberExpression, valueExpression, stringMatch);
    }

    private static Expression BuildPresence<T>(PresenceNode node, ParameterExpression parameter, QueryFieldMap<T> fieldMap)
    {
        // Presence runs through the same per-field whitelist as every other operator: a field must
        // declare AllowFilter(FilterOperator.Present) for a presence test to resolve at all.
        var mapping = fieldMap.GetField(node.Field, FilterOperator.Present);
        var memberExpression = EntityMemberBinder.Bind(mapping.EntityExpression, parameter);

        // A non-nullable value type always holds a value, so presence is a constant truth. Anything
        // that can be null — a reference type or a Nullable<T> — is present exactly when it is not
        // null, which translates to `col IS NOT NULL` under EF Core: a bound, parameter-free predicate.
        if (memberExpression.Type.IsValueType && Nullable.GetUnderlyingType(memberExpression.Type) is null)
            return Expression.Constant(true);

        return Expression.NotEqual(memberExpression, Expression.Constant(null, memberExpression.Type));
    }

    // Projects the deserialized value through a member access on a captured carrier object instead of
    // baking it into an Expression.Constant. EF Core's parameter extraction lifts a member access on a
    // captured instance into a query parameter (@p), whereas a constant is emitted as a literal into the
    // SQL text. Parameterizing means distinct filter values produce one identical, cacheable SQL string,
    // so the plan cache and any compiled-query cache are reused across requests on the hottest read path.
    private static Expression BuildValueAccessor(object? value, Type propertyType)
    {
        // A single non-generic holder avoids MakeGenericType/Activator per node (Compile runs per request
        // on the hot read path); the Convert restores the static type EF needs and unboxes value types.
        var carrier = new ValueCarrier(value);
        var carrierConstant = Expression.Constant(carrier);
        // Use the cached PropertyInfo overload of Expression.Property; the string-name overload does a
        // Type.GetProperty reflection lookup on every call, and Compile runs per comparison node per request.
        var valueAccess = Expression.Property(carrierConstant, ValueProperty);
        return Expression.Convert(valueAccess, propertyType);
    }

    private static Expression BuildStringComparison(FilterOperator op, Expression member, Expression value, FilterStringMatch stringMatch)
    {
        var (methodName, negated) = ResolveStringMethod(op);

        // Collation mode leaves the column bare so a case-insensitive column collation can carry the
        // case-folding and an index on the column stays usable, but that means the string method is called
        // on the raw member: a null member would throw a NullReferenceException in-memory / client-eval,
        // and a null filter value would produce member.Method(null) => ArgumentNullException.
        // Guard the member with a null-check that keeps the column bare in the LIKE (`col IS NOT NULL AND
        // col LIKE ...` still uses the index), and coalesce only the VALUE operand: the value is a
        // parameter (the ValueCarrier), NOT the column, so `value ?? ""` guards a null filter value without
        // wrapping the column, keeping the LIKE sargable.
        // Semantics: Collation mode matches SQL semantics — a null column does not match (`col LIKE @p`
        // with col NULL yields no match), which is the whole point of the opt-in. This differs from
        // LowerInvariant's vacuous-true on a null member with an empty pattern ("".Contains("") == true);
        // for a null member positive ops => false and Not* => true here.
        if (stringMatch == FilterStringMatch.Collation)
        {
            var safeValue = Expression.Coalesce(value, Expression.Constant(string.Empty));
            var call = Expression.Call(member, methodName, null, safeValue);
            return negated
                ? Expression.OrElse(Expression.Equal(member, NullString), Expression.Not(call))
                : Expression.AndAlso(Expression.NotEqual(member, NullString), call);
        }

        var lowered = Expression.Call(ToLowerNullSafe(member), methodName, null, ToLowerNullSafe(value));
        return negated ? Expression.Not(lowered) : lowered;
    }

    private static (string MethodName, bool Negated) ResolveStringMethod(FilterOperator op) => op switch
    {
        FilterOperator.Contains => (nameof(string.Contains), false),
        FilterOperator.NotContains => (nameof(string.Contains), true),
        FilterOperator.StartsWith => (nameof(string.StartsWith), false),
        FilterOperator.NotStartsWith => (nameof(string.StartsWith), true),
        FilterOperator.EndsWith => (nameof(string.EndsWith), false),
        FilterOperator.NotEndsWith => (nameof(string.EndsWith), true),
        _ => throw new InvalidOperationException($"Unsupported string operator: {op}")
    };

    private static Expression BuildValueComparison(FilterOperator op, Expression member, Expression value, FilterStringMatch stringMatch)
    {
        if (member.Type == typeof(string) && stringMatch == FilterStringMatch.LowerInvariant)
        {
            member = ToLowerNullSafe(member);
            value = ToLowerNullSafe(value);
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

    // Lowercases a string expression after coalescing null to an empty string, so case-insensitive
    // comparisons never dereference a null entity property (which would throw a NullReferenceException
    // during in-memory evaluation). Translates to LOWER(COALESCE(col, '')) under EF Core.
    private static Expression ToLowerNullSafe(Expression stringExpression)
    {
        var coalesced = Expression.Coalesce(stringExpression, Expression.Constant(string.Empty));
        return Expression.Call(coalesced, ToLowerMethod);
    }

    private static ValidationException PresenceAsComparison(string field) =>
        new(new Dictionary<string, string[]>
        {
            ["filter"] = [$"The 'pr' (present) operator is a presence test on '{field}': use a 'present' node, not a 'comparison'."]
        });

    private static ValidationException NullValueOnNonNullableField(string field) =>
        new(new Dictionary<string, string[]>
        {
            ["filter"] = [$"'{field}' can never be null, so it cannot be compared with null. Use the 'present' node to ask whether a nullable field has a value."]
        });

    private static ValidationException NullWithNonEqualityOperator(FilterOperator op, string field) =>
        new(new Dictionary<string, string[]>
        {
            ["filter"] = [$"Operator '{op}' cannot be applied to a null value on '{field}'. Only 'eq' and 'neq' compare with null; use the 'present' node to ask whether the field has a value."]
        });

    private static ValidationException StringOperatorOnNonStringField(FilterOperator op, string field) =>
        new(new Dictionary<string, string[]>
        {
            ["filter"] = [$"Operator '{op}' can only be applied to a string field, but '{field}' is not a string."]
        });

    private static bool IsStringOperation(FilterOperator op)
    {
        return op is FilterOperator.Contains or FilterOperator.NotContains
            or FilterOperator.StartsWith or FilterOperator.NotStartsWith
            or FilterOperator.EndsWith or FilterOperator.NotEndsWith;
    }

    private static object? DeserializeValue(JsonElement element, Type targetType)
    {
        return element.Deserialize(targetType);
    }

    /// <summary>
    /// A minimal non-generic holder whose sole purpose is to give EF Core's parameter extraction a member
    /// access on a captured instance to lift into a query parameter. Holds <see cref="object"/> so no
    /// per-node generic construction is needed; the caller restores the static type with a Convert. See
    /// <see cref="BuildValueAccessor"/>.
    /// </summary>
    /// <param name="Value">The filter value being carried, boxed as <see cref="object"/>.</param>
    private sealed record ValueCarrier(object? Value);
}
