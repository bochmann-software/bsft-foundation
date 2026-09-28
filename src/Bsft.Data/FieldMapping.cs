using Bsft.Data.Contracts.Filtering;
using System.Linq.Expressions;

namespace Bsft.Data;

/// <summary>
/// Represents a resolved mapping from a DTO field to an entity expression.
/// </summary>
/// <param name="EntityExpression">The lambda expression targeting the entity property.</param>
/// <param name="AllowedOperators">
/// The set of allowed filter operators. Read-only: a mapping is the answer to "what may be asked of
/// this field", and a caller that could add an operator to it after the map was built would widen
/// what the query surface accepts without anyone having declared it.
/// </param>
/// <param name="IsSortable">Whether this field supports sorting.</param>
public sealed record FieldMapping(
    LambdaExpression EntityExpression,
    IReadOnlySet<FilterOperator> AllowedOperators,
    bool IsSortable);
