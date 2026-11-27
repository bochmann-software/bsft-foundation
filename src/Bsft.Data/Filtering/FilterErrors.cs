using Bsft.Core;
using Bsft.Data.Contracts.Filtering;

namespace Bsft.Data.Filtering;

/// <summary>
/// The client mistakes a compiled filter refuses, each a <see cref="ValidationException"/> keyed on
/// <c>filter</c> (a 400). Shared by <see cref="FilterCompiler"/> and <see cref="DynamicFieldCompiler"/> so a
/// declared and a dynamic field refuse the same request with the same message.
/// </summary>
internal static class FilterErrors
{
    internal static ValidationException UnknownField(string field) =>
        Filter($"Unknown filter field: '{field}'.");

    internal static ValidationException OperatorNotAllowed(FilterOperator op, string field) =>
        Filter($"Operator '{op}' is not allowed on field '{field}'.");

    internal static ValidationException PresenceAsComparison(string field) =>
        Filter($"The 'pr' (present) operator is a presence test on '{field}': use a 'present' node, not a 'comparison'.");

    internal static ValidationException NullValueOnNonNullableField(string field) =>
        Filter($"'{field}' can never be null, so it cannot be compared with null. Use the 'present' node to ask whether a nullable field has a value.");

    internal static ValidationException NullWithNonEqualityOperator(FilterOperator op, string field) =>
        Filter($"Operator '{op}' cannot be applied to a null value on '{field}'. Only 'eq' and 'neq' compare with null; use the 'present' node to ask whether the field has a value.");

    internal static ValidationException StringOperatorOnNonStringField(FilterOperator op, string field) =>
        Filter($"Operator '{op}' can only be applied to a string field, but '{field}' is not a string.");

    internal static ValidationException SetElementOfWrongType(string field, Type targetType) =>
        Filter($"The 'in' set on '{field}' holds a value that is not a {targetType.Name}.");

    private static ValidationException Filter(string message) =>
        new(new Dictionary<string, string[]> { ["filter"] = [message] });
}
