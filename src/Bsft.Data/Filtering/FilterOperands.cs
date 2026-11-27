using System.Linq.Expressions;
using System.Reflection;

namespace Bsft.Data.Filtering;

/// <summary>
/// Builds the operands a compiled filter compares: a parameterized filter value and a case-folded string
/// member. Shared by <see cref="FilterCompiler"/> and <see cref="FilterSetCompiler"/>, so a single value and
/// a set of values reach SQL the same way.
/// </summary>
internal static class FilterOperands
{
    private static readonly MethodInfo ToLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes)!;

    private static readonly PropertyInfo ValueProperty = typeof(ValueCarrier).GetProperty(nameof(ValueCarrier.Value))!;

    // Projects the deserialized value through a member access on a captured carrier object instead of
    // baking it into an Expression.Constant. EF Core's parameter extraction lifts a member access on a
    // captured instance into a query parameter (@p), whereas a constant is emitted as a literal into the
    // SQL text. Parameterizing means distinct filter values produce one identical, cacheable SQL string,
    // so the plan cache and any compiled-query cache are reused across requests on the hottest read path.
    internal static Expression Parameterize(object? value, Type propertyType)
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

    // Lowercases a string expression after coalescing null to an empty string, so case-insensitive
    // comparisons never dereference a null entity property (which would throw a NullReferenceException
    // during in-memory evaluation). Translates to LOWER(COALESCE(col, '')) under EF Core.
    internal static Expression ToLowerNullSafe(Expression stringExpression)
    {
        var coalesced = Expression.Coalesce(stringExpression, Expression.Constant(string.Empty));
        return Expression.Call(coalesced, ToLowerMethod);
    }

    /// <summary>
    /// A minimal non-generic holder whose sole purpose is to give EF Core's parameter extraction a member
    /// access on a captured instance to lift into a query parameter. Holds <see cref="object"/> so no
    /// per-node generic construction is needed; the caller restores the static type with a Convert. See
    /// <see cref="Parameterize"/>.
    /// </summary>
    /// <param name="Value">The filter value being carried, boxed as <see cref="object"/>.</param>
    private sealed record ValueCarrier(object? Value);
}
