using Bsft.Data.Contracts.Filtering;
using System.Linq.Expressions;
using System.Text.Json;

namespace Bsft.Data.Filtering;

/// <summary>
/// Compiles <see cref="FilterOperator.In"/> on a declared field.
/// </summary>
/// <remarks>
/// <para>
/// The member is one of the values. A set means what an <c>or</c> over one <c>eq</c> per element
/// means, on every provider and in memory, including what <c>eq</c> means for a missing value:
/// </para>
/// <list type="bullet">
/// <item><description>A field whose comparison is not case-folded (a non-string, or a string under
/// <see cref="FilterStringMatch.Collation"/>) is sent as one parameterized collection, which
/// PostgreSQL reads as <c>= ANY(@p)</c> on EF Core 9 and 10 and SQL Server and SQLite read as one
/// parameter per value on EF Core 10. A <c>NULL</c> column is in no set.</description></item>
/// <item><description>A string under <see cref="FilterStringMatch.LowerInvariant"/> is compiled as the
/// <c>or</c> over <c>eq</c> itself, each element folded the way <c>eq</c> folds its value. Lowering the
/// values here once and the column in the query would use two different foldings (the invariant one
/// here, the current culture's in memory, the database's in SQL), and a set could then miss what
/// <c>eq</c> matches, as <c>"I"</c> does under tr-TR. Like <c>eq ""</c>, <c>in [""]</c> matches a
/// <c>NULL</c> column in this mode, since <c>eq</c> folds a missing value to the empty string.</description></item>
/// </list>
/// <para>
/// <see cref="FilterInValues"/> has already refused a <c>null</c> element, an empty set and a set over
/// its limit.
/// </para>
/// </remarks>
internal static class FilterSetCompiler
{
    internal static Expression In(ComparisonNode node, Expression member, FilterStringMatch stringMatch)
    {
        var elements = node.Read();

        if (member.Type == typeof(string) && stringMatch == FilterStringMatch.LowerInvariant)
            return FoldedOrOverEq(node.Field, elements, member);

        var values = Array.CreateInstance(member.Type, elements.Count);
        for (var i = 0; i < elements.Count; i++)
            values.SetValue(DeserializeElement(node.Field, elements[i], member.Type), i);

        var valuesExpression = FilterOperands.Parameterize(values, values.GetType());
        return Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [member.Type], valuesExpression, member);
    }

    // The same expression `eq` builds for a string in LowerInvariant mode, once per element: both
    // operands folded by FilterOperands.ToLowerNullSafe, the value a parameter.
    private static Expression FoldedOrOverEq(string field, IReadOnlyList<JsonElement> elements, Expression member)
    {
        var foldedMember = FilterOperands.ToLowerNullSafe(member);
        return elements
            .Select(element => (Expression)Expression.Equal(
                foldedMember,
                FilterOperands.ToLowerNullSafe(
                    FilterOperands.Parameterize(DeserializeElement(field, element, typeof(string)), typeof(string)))))
            .Aggregate(Expression.OrElse);
    }

    private static object? DeserializeElement(string field, JsonElement element, Type targetType)
    {
        try
        {
            return element.Deserialize(targetType);
        }
        catch (JsonException)
        {
            throw FilterErrors.SetElementOfWrongType(field, targetType);
        }
    }
}
