using System.Text.Json.Serialization;

namespace Bsft.Data.Contracts.Filtering;

/// <summary>
/// Defines the comparison operators available for query filtering.
/// </summary>
[JsonConverter(typeof(FilterOperatorJsonConverter))]
public enum FilterOperator
{
    /// <summary>Equals.</summary>
    Eq,

    /// <summary>Not equals.</summary>
    Neq,

    /// <summary>Greater than.</summary>
    Gt,

    /// <summary>Greater than or equal.</summary>
    Gte,

    /// <summary>Less than.</summary>
    Lt,

    /// <summary>Less than or equal.</summary>
    Lte,

    /// <summary>Contains (string).</summary>
    Contains,

    /// <summary>Does not contain (string).</summary>
    NotContains,

    /// <summary>Starts with (string).</summary>
    StartsWith,

    /// <summary>Does not start with (string).</summary>
    NotStartsWith,

    /// <summary>Ends with (string).</summary>
    EndsWith,

    /// <summary>Does not end with (string).</summary>
    NotEndsWith,

    /// <summary>
    /// Present — the field has a value (is not null). Unary: it takes no comparison value.
    /// Mirrors the SCIM (RFC 7644 §3.4.2.2) <c>pr</c> operator. Carried by <see cref="PresenceNode"/>
    /// rather than <see cref="ComparisonNode"/>, since presence compares against nothing.
    /// </summary>
    Present,

    /// <summary>
    /// Set membership — the field equals one of the values of a JSON array (<c>{"op": "in", "value": [...]}</c>),
    /// translated to <c>= ANY(@p)</c> on PostgreSQL. It means exactly what an <c>or</c> over one
    /// <see cref="Eq"/> per element means, so a missing or null field is in no set.
    /// </summary>
    /// <remarks>
    /// The array holds at least one value and no <c>null</c>: "not set" has one canonical form,
    /// <c>{field, eq, null}</c>, and a set that names it or names nothing is refused as a bad request
    /// rather than read one way here and another way in SQL. Unlike the other operators, a field never
    /// accepts <c>in</c> implicitly; it has to name it in its own operator list. Declared last so the
    /// numeric values of the operators before it stay what they were.
    /// </remarks>
    In
}
