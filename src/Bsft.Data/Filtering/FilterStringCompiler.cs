using Bsft.Data.Contracts.Filtering;
using System.Linq.Expressions;

namespace Bsft.Data.Filtering;

/// <summary>
/// Compiles the string operators (<c>co</c>, <c>sw</c>, <c>ew</c> and their negations) on a declared
/// string field, in either <see cref="FilterStringMatch"/> mode.
/// </summary>
internal static class FilterStringCompiler
{
    private static readonly ConstantExpression NullString = Expression.Constant(null, typeof(string));

    internal static Expression Compare(FilterOperator op, Expression member, Expression value, FilterStringMatch stringMatch)
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

        var lowered = Expression.Call(FilterOperands.ToLowerNullSafe(member), methodName, null, FilterOperands.ToLowerNullSafe(value));
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

    internal static bool IsStringOperation(FilterOperator op)
    {
        return op is FilterOperator.Contains or FilterOperator.NotContains
            or FilterOperator.StartsWith or FilterOperator.NotStartsWith
            or FilterOperator.EndsWith or FilterOperator.NotEndsWith;
    }
}
